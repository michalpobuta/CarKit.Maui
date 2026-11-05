# Diagnostics reference

## MSBuild diagnostics (CARKIT001–CARKIT009)

The MSBuild targets of `CarKit.Maui` report configuration problems while the app builds. Build with `-warnaserror` in CI
to treat the warnings as errors.

| Code | Level | When | What to do |
| --- | --- | --- | --- |
| `CARKIT001` | Warning | A category has no counterpart on the platform being built (for example `Iot` on iOS, `QuickFoodOrdering` on Android) | Nothing if the app supports one platform for this category on purpose |
| `CARKIT002` | Error | `CarKitCategories` is missing or has an unknown category, or `CarKitAndroidMinCarApiLevel` is outside 1–8 | Fix the property; 8 is the highest Car App API level of the current binding |
| `CARKIT003` | — | Retired: reserved for future categories with an unverified entitlement key | — |
| `CARKIT004` | Error | A feature is not available yet: `CarKitEnableAutomotive` (no `Car.App.Automotive` binding), `CarKitAndroidCalling` (no Core-Telecom binding) | Remove the property |
| `CARKIT005` | Warning | Category `Audio` without the `CarKit.Maui.Media` package (no media service and no Now Playing bridge) | Add `CarKit.Maui.Media` |
| `CARKIT006` | Warning | The app's `Info.plist` already has `UIBackgroundModes` without `audio`; the SDK replaces arrays, so CarKit cannot add its value (`Audio`, or `Navigation` with its module) | Add `audio` to `UIBackgroundModes` in `Info.plist`, or set `CarKitIosBackgroundAudio=false` |
| `CARKIT007` | Warning | Category `Communication` without `CarKit.Maui.Communication` (no `MessagingStyle` notifications, Siri or CallKit) | Add `CarKit.Maui.Communication` |
| `CARKIT008` | Warning | Category `Navigation` without `CarKit.Maui.Navigation` (no map, navigation session or voice prompts) | Add `CarKit.Maui.Navigation` |
| `CARKIT009` | Error | `CarKitIosDashboard`, `CarKitIosInstrumentCluster` or `CarKitAndroidCluster` without the `Navigation` category and the `CarKit.Maui.Navigation` package | Add both or remove the property |

## Experimental API (CARKIT0001, CARKIT0002)

Experimental APIs carry `[Experimental]`, so using them is a compiler error until you opt in:

| ID | API | Why it is experimental |
| --- | --- | --- |
| `CARKIT0001` | `CarPlayVideoBrowseTemplate`, `CarNowPlayingTemplate.AllowsMiniPlayer`, `CarVoiceControlTemplate.IsOverlay` | iOS 27 features built from the Xcode 27 beta headers and WWDC26 material without the iOS 27 SDK; names and behavior may change when CarKit adds the iOS 27 build |
| `CARKIT0002` | `ICarMapSurface.UseMauiView` | A MAUI view on the car screen has no performance guarantees |

Opt in per file or per project:

```csharp
#pragma warning disable CARKIT0001
var videos = new CarPlayVideoBrowseTemplate("Videos") { Sections = [...] };
#pragma warning restore CARKIT0001
```

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CARKIT0001</NoWarn>
</PropertyGroup>
```

## Run-time diagnostics

- Exceptions: see [Concepts](concepts.md#exceptions). Exceptions from your callbacks and hooks go to `ICarErrorHandler`.
- Logging: CarKit logs through `ILogger` with categories that start with `CarKit.` (for example `CarKit.Session`,
  `CarKit.Hosting`, `CarKit.Navigation`). Validation warnings in `Warn` mode, removed platform features (`Information`) and
  failed images (`Warning`) appear there.
- `car.Diagnostics.LogNativeCalls = true` logs every native call of the renderers; use it in Debug builds.
- `nav.LogFrameTimes = true` logs map frame statistics every 120 frames.
