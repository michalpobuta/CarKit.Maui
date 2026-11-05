# Validation and limits

CarPlay and Android Auto reject templates at run time when a category does not allow them, when a stack is too deep or
when a template has too many items. CarKit checks every template before it reaches the car and reports the problem in C#.

## What the validator checks

- The template is allowed in at least one of the app's categories on the connected platform.
- The stack depth for the categories (the root counts; a modal shown over a template does not).
- The children of a `CarTabTemplate` (each tab must be a template the platform allows in a tab).
- Item limits: list rows and sections, grid buttons, tabs, information items, points of interest, map buttons, actions.
- The minimum platform version of the template (for example `CarTabTemplate` needs iOS 14 and Car App API level 6).
- Model rules: unique `Id`s inside a template, a distance for every Android Auto point of interest, at least one trailing
  bar button on an Android Auto map template, voice control states with unique identifiers.

The rules are data (`Rules/*.json` embedded in `CarKit.Maui.Core`), so they can follow the yearly platform changes
without changes to the renderers.

### CarPlay templates per category

| Category | List | Grid | Tab | Info | POI | Search | Map | Now Playing | Contact | Alert / Action sheet | Depth |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Audio | ✓ | ✓ | ✓ | – | – | – | – | ✓ | – | ✓ | 5 |
| Communication | ✓ | ✓ | ✓ | ✓ | – | – | – | – | ✓ | ✓ | 5 |
| Navigation | ✓ | ✓ | ✓ | ✓ | – | ✓ | ✓ | – | – | ✓ | 5 |
| Parking / EV charging / Fueling | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | – | – | ✓ | 5 (Fueling 3) |
| Quick food ordering | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | – | – | ✓ | 3 |
| Driving task | ✓ | ✓ | ✓ | ✓ | ✓ | – | – | – | – | ✓ | 3 |

Voice-based conversational apps (iOS 26.4) start with a voice control template and have a depth of 3. From iOS 27,
voice control is available in every category. With several categories, the most permissive rule applies.

## Validation modes

`car.Validation` decides what happens when a template breaks a rule:

| Mode | Behavior |
| --- | --- |
| `Strict` | The operation throws (`CarTemplateNotAllowedException`, `CarTemplateLimitException`, …) |
| `Warn` | CarKit logs a warning and sends the template anyway; the platform may still reject it |
| `Off` | No validation |

The default is `Strict`. The MSBuild targets set `Warn` for `Release` builds through the `CarKitValidation` property
(`CarKit.Maui.Validation` runtime switch), so a rule that changed in a new platform release does not stop a shipped app.
Set `<CarKitValidation>Strict</CarKitValidation>` to keep `Strict` in Release.

Two kinds of checks run in every mode, because they only adapt the template to the car:

- Fallbacks of templates the platform does not have ([Templates](templates.md#fallbacks)).
- The feature gate: properties of a newer platform release that the car or the CarKit build cannot render are removed
  and logged at `Information` level ([Platform features](platform-features.md)).

## Item limits

CarKit reads the limits from the platform at run time, once per connection:

- CarPlay: `CPListTemplate.MaximumItemCount` and `MaximumSectionCount`, `CPGridTemplate.MaximumItems`,
  `CPTabBarTemplate.MaximumTabCount` and `CPAlertTemplate.MaximumActionCount`. These values depend on the app's
  entitlements and the iOS version: on iOS 26 an app with the audio entitlement gets 1 alert action and 500 list items
  (4 tabs on iOS 26.0, 5 on iOS 26.4), while other categories get 3 alert actions, 5 tabs and 12 to 500 list items.
- Android Auto (Car App API 2+): `ConstraintManager.GetContentLimit` for lists, grids, panes and place lists.

When a value is missing, CarKit uses the default from its rules (for example 12 points of interest on CarPlay).
An app that declares `Audio` next to other categories gets the limits of the audio entitlement, for example 1 alert action.
`car.Limits.OnOverflow` decides what happens above a limit:

- `Truncate` (default): CarKit drops the items above the limit, so the first items stay. Put the primary action of an
  alert first: CarPlay on its own would keep the last actions.
- `Throw`: a `CarTemplateLimitException` under the validation mode (it throws only in `Strict`).

## Android Auto task steps

Android Auto limits how many templates a task may show: at most five, and the fifth must end the task (a pane, a message,
a long message or a sign-in template). Going back gives the steps of the screens that were removed. CarKit counts the
steps like the host:

- `PushAsync` of a new template costs a step.
- `UpdateAsync` of the same template is a refresh and costs nothing when the host treats it as one: for a list, the
  previous version was loading, or the title, sections, headers, row count and row titles are the same; similar rules
  apply to grids, panes, messages and tabs. A search template refresh is always free.
- `CarLongMessageTemplate` and `AndroidAutoSignInTemplate` do not count. `NavigationTemplate` (the map during guidance)
  resets the count, as the host does.

In `Strict` mode, a push or update that would exceed the limit throws `CarTemplateLimitException` before the host shows
its error screen. The counter is conservative where Google does not document the rules (screen removal and
`SectionedItemTemplate`), so verify long flows in the Desktop Head Unit.
