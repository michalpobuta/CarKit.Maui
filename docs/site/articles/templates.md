# Templates

CarKit has 16 template types: 10 shared by both platforms, 3 with an automatic fallback on the other platform and 3 that
exist on one platform only. The categories of the app decide which of them the car allows
([Validation and limits](validation.md)).

## Catalog

| CarKit type | CarPlay | Android Auto | Notes |
| --- | --- | --- | --- |
| `CarListTemplate` | `CPListTemplate` | `ListTemplate`; `SectionedItemTemplate` on Car App API 8 | Sections, headers, image rows, playback indicator, toggles (Android Auto), details header and item playback state (iOS 26.4) |
| `CarGridTemplate` | `CPGridTemplate` | `GridTemplate` | CarPlay: up to 8 buttons |
| `CarTabTemplate` | `CPTabBarTemplate` | `TabTemplate` (Car App API 6) | CarPlay: root only. Android Auto: 2–4 tabs, each with an icon (CarKit uses the app icon when a tab has none) |
| `CarInformationTemplate` | `CPInformationTemplate` | `PaneTemplate` | Label–value pairs and actions |
| `CarSearchTemplate` | `CPSearchTemplate` | `SearchTemplate` | `OnSearchTextChanged` returns results, `OnSearchSubmitted` handles the search button |
| `CarPointOfInterestTemplate` | `CPPointOfInterestTemplate` | `PlaceListMapTemplate` (host map, POI categories), a list of places (other categories) or `MapWithContentTemplate` with the app's map (Navigation module, Car App API 7) | CarPlay: up to 12 points. Android Auto needs a distance for each point |
| `CarAlertTemplate` (modal) | `CPAlertTemplate` | `AppManager.ShowAlert` (Car App API 5); below that a message screen | Choosing an action closes the alert. CarPlay shows at most `CPAlertTemplate.MaximumActionCount` actions (1 for audio apps, 3 for the others on iOS 26), and CarKit keeps the first ones. Android Auto closes the alert after `Duration` (10 s by default) |
| `CarMessageTemplate` | `CPInformationTemplate` | `MessageTemplate` | Message, icon (Android Auto), up to two actions, loading state |
| `CarActionSheetTemplate` (modal) | `CPActionSheetTemplate` | Fallback: message screen with the actions | Choosing an action closes the sheet |
| `CarLongMessageTemplate` | Fallback: `CPInformationTemplate` | `LongMessageTemplate` (Car App API 2) | Android Auto shows it only while parked |
| `CarVoiceControlTemplate` (modal) | `CPVoiceControlTemplate` | Fallback: message screen of the active state | States switched in place; animated state images; action buttons (iOS 26.4) |
| `CarNowPlayingTemplate` | `CPNowPlayingTemplate.SharedTemplate` | System media UI from `MediaSession`; `MediaPlaybackTemplate` (Car App API 8) behind a flag | [Media module](media.md) |
| `CarMapTemplate` | `CPMapTemplate` over the car window | `NavigationTemplate`; `MapWithContentTemplate` for route previews | [Navigation module](navigation.md) |
| `CarPlayContactTemplate` | `CPContactTemplate` | — | Call, message, directions and custom buttons; category Communication |
| `AndroidAutoSignInTemplate` | — | `SignInTemplate` (PIN, QR code from Car App API 4, password, provider) | Parked only; does not count as a task step |
| `CarPlayVideoBrowseTemplate` | `CPListTemplate` with a details header and video playback state | — | iOS 27, category Video. Experimental (`CARKIT0001`) |

The Navigation module also drives the CarPlay Dashboard and instrument cluster and the Android Auto cluster
([Navigation](navigation.md)).

## Rows, buttons and actions

| CarKit element | CarPlay | Android Auto |
| --- | --- | --- |
| `CarListItem` | `CPListItem` (text, detail, image, accessory, playback, explicit) | `Row` (title, detail, image, `Toggle`, browsable, place metadata) |
| `CarListImageRowItem` | `CPListImageRowItem` | `GridSection` in `SectionedItemTemplate` (API 8), otherwise a `Row` with the first image |
| `CarConversationItem` | `CPMessageListItem` (selecting it hands over to Siri) | `ConversationItem` (Car App API 7), otherwise a `Row` with the last message |
| `CarGridItem` | `CPGridButton` | `GridItem` |
| `CarAction` | `CPAlertAction`, `CPTextButton` | `Action` |
| `CarBarButton` | `CPBarButton` (leading and trailing) | Header actions (Car App API 7) or `ActionStrip` |
| `CarMapButton` | `CPMapButton` | Map action strip |

Some properties exist on one platform only. CarKit ignores them on the other platform:

- `LeadingBarButtons` are CarPlay only; Android Auto shows at most 2 trailing header actions, at most one of them with a
  title.
- A row with a `Toggle` has no click listener on Android Auto (a `Row` rule), so its `OnSelected` runs on CarPlay only.
- Playback progress, the explicit flag and the accessory have no Car App Library 1.7 equivalent.
- An action with `IsEnabled = false` is left out of CarPlay information templates (`CPTextButton` has no disabled state).

## Fallbacks

Built-in fallbacks keep the `Id`, the tab information and the bar buttons of the original template:

| Template | Fallback |
| --- | --- |
| `CarActionSheetTemplate` on Android Auto | Message screen with the actions; choosing one closes it |
| `CarLongMessageTemplate` on CarPlay | `CPInformationTemplate` |
| `CarVoiceControlTemplate` on Android Auto | Message screen with the title, text and icon of the active state |
| `CarAlertTemplate` on Android Auto below Car App API 5 | Message screen |

An explicit fallback (`template.WithFallback(other)`) wins over the built-in one. A copy made with `with` keeps the
fallback of the original, so update the fallback too when its content depends on the template.
