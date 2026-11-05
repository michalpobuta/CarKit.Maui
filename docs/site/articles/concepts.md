# Concepts: sessions, the template stack and updates

## The handler and the session

The app implements one <xref:CarKit.Maui.ICarAppHandler>. CarKit calls `OnConnectedAsync` with an
<xref:CarKit.Maui.ICarSession> each time a car connects and `OnDisconnectedAsync` when it disconnects:

```csharp
public sealed class MyCarAppHandler(StationsService stations) : ICarAppHandler
{
    public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        var items = (await stations.GetNearbyAsync(ct)).Select(s => new CarListItem(s.Name)
        {
            Detail = s.Address,
            IsBrowsable = true,
            OnSelected = _ => session.PushAsync(new CarInformationTemplate(s.Name)
            {
                Items = [new CarInformationItem("Free spots", $"{s.FreeSlots}")],
                Actions = [new CarAction("Navigate", _ => stations.NavigateAsync(s))],
            }),
        });

        await session.SetRootAsync(new CarListTemplate("Nearby") { Sections = [new CarSection(items)] });
    }
}
```

The handler is a singleton in the app's services, so it can take its dependencies through the constructor.

A car session can start without the phone UI: CarPlay connects the car scene while the iPhone is locked, and Android
Auto starts the `CarAppService` without an `Activity`. CarKit makes sure the `MauiApp` and its services exist in both
cases.

`session.Platform` tells you where the session runs (`CarPlay`, `AndroidAuto`), `session.Capabilities` what the car and
the CarKit build support, and `session.ContentStyle` whether the car shows a light or a dark interface
(`ContentStyleChanged` reports changes).

## The template stack

The API follows the platforms' navigation model:

| Method | Meaning |
| --- | --- |
| `SetRootAsync(template)` | Replace the whole stack with one root template |
| `PushAsync(template)` / `PopAsync()` | Push a template on top / remove the top one |
| `PopToAsync(template)` / `PopToRootAsync()` | Go back to a template in the stack / to the root |
| `PresentAsync(modal)` / `DismissAsync()` | Show or close a modal (`CarAlertTemplate`, `CarActionSheetTemplate`, `CarVoiceControlTemplate`) |
| `UpdateAsync(template)` | Show new content for a template already in the stack or presented |

`session.Stack` and `session.Presented` always describe the app's model. The car's back button pops templates, and the
session reports it through `TemplateDisappeared`. Pushing the same template twice or pushing a modal throws
<xref:CarKit.Maui.CarStackException>.

When the car disconnects, CarKit discards everything it created on the platform but keeps the stack, so the app can
rebuild it in the next `OnConnectedAsync` (for example from `session.Stack` of the previous session or its own state).

## Immutable templates and updates

Templates, sections, rows and actions are C# records with `init` properties. Every element has a stable `Id` (a GUID
unless you set one). To change a screen, create a copy with `with` and pass it to `UpdateAsync`:

```csharp
var list = new CarListTemplate("Parking") { Sections = [new CarSection(rows)] };
await session.SetRootAsync(list);

// later
await session.UpdateAsync(list with { Sections = [new CarSection(newRows)] });
```

The updated template keeps the `Id`, so CarKit knows which screen to change; a different template type throws
<xref:CarKit.Maui.CarStackException>. Collections are `CarArray<T>`, an immutable array with structural equality, so
CarKit can compute the difference between two versions:

- **CarPlay**: CarKit changes the existing `CP*` objects in place (row properties, `UpdateSections`, grid buttons,
  information items). It replaces the native template only where CarPlay has no API for the change, such as the title of
  a list.
- **Android Auto**: CarKit rebuilds the template in `Screen.OnGetTemplate()` after `Invalidate()`. When the change
  counts as a refresh under Car App Library rules, it does not use a step of the task (see
  [Validation and limits](validation.md#android-auto-task-steps)).

## Callbacks and threads

Handlers such as `OnSelected` have the type `Func<CarItemContext, Task>`. The context gives the session, the element and
a cancellation token. On CarPlay, a row keeps its spinner until the task completes.

All native calls go through the main thread, and CarKit calls your handlers on the main thread too. Exceptions from
handlers go to `ICarErrorHandler` instead of crashing the car session.

## Templates of one platform

Shared templates have no prefix (`CarListTemplate`). Templates that exist on one platform only have the platform prefix
(`CarPlayContactTemplate`, `AndroidAutoSignInTemplate`). `car.UnsupportedTemplateBehavior` decides what happens when a
template does not exist on the connected platform:

- `UseFallbackOrThrow` (default): CarKit shows `template.WithFallback(other)`, a built-in fallback (action sheet,
  alert, voice control and long message have one), or throws <xref:CarKit.Maui.CarTemplateNotSupportedException>.
- `Throw`: always throws <xref:CarKit.Maui.CarTemplateNotSupportedException>.

`session.Capabilities.Supports<TTemplate>()` answers the question before you push.

## Native objects (escape hatch)

When CarKit does not model something, configure the native object directly. The hooks run after CarKit builds the
`CP*` object (CarPlay) or just before it calls `Build()` on the Car App Library builder (Android Auto), on every
render:

```csharp
var stations = new CarListTemplate("Stations") { Sections = [...] };
#if IOS
stations = stations.ConfigureCarPlay(cp => cp.EmptyViewTitleVariants = ["No stations"]);   // CPListTemplate
#elif ANDROID
stations = stations
    .ConfigureAndroidAuto(b => b.AddAction(mapButton))                                      // ListTemplate.Builder (Car App API < 8)
    .ConfigureAndroidAutoSectioned(b => b.AddAction(mapButton));                            // SectionedItemTemplate.Builder (API 8)
#endif
```

`ConfigureCarPlay` exists only in the iOS builds of `CarKit.Maui` and of the modules, and `ConfigureAndroidAuto` only in
the Android builds, so keep the calls in `#if` blocks or in `Platforms/`. A hook must be fast and idempotent, must not call the session, and must not change what Android Auto compares for refreshes
(title, sections, rows, loading state). Exceptions from hooks go to `ICarErrorHandler` and rendering continues.
`session.GetNative<TNative>()` returns the `CPInterfaceController` (CarPlay) or the `CarContext` (Android Auto).

## Exceptions

All CarKit exceptions derive from <xref:CarKit.Maui.CarKitException>:

| Exception | When |
| --- | --- |
| <xref:CarKit.Maui.CarNotConnectedException> | The car disconnected during the operation |
| <xref:CarKit.Maui.CarTemplateNotAllowedException> | The template is not allowed in the app's categories |
| <xref:CarKit.Maui.CarTemplateNotSupportedException> | The platform or its version does not have the template and there is no fallback |
| <xref:CarKit.Maui.CarTemplateLimitException> | Too many items, too deep a stack, or too many Android Auto task steps |
| <xref:CarKit.Maui.CarStackException> | Invalid stack operation, for example pushing the same template twice |
| <xref:CarKit.Maui.CarPlatformException> | The platform rejected the call (wraps the native error) |
