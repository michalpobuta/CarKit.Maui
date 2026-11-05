# Navigation

`CarKit.Maui.Navigation` adds turn-by-turn navigation (category `Navigation`): a map drawn by the app on every car
screen, one navigation session shared by the phone and the cars, voice prompts, and the CarPlay Dashboard and instrument
cluster. It does not draw map tiles: use a map SDK or your own drawing code.

```xml
<CarKitCategories>Navigation</CarKitCategories>
<CarKitIosDashboard>true</CarKitIosDashboard>
<CarKitIosInstrumentCluster>true</CarKitIosInstrumentCluster>
<CarKitAndroidCluster>true</CarKitAndroidCluster>
```

```csharp
using CarKit.Maui;
using CarKit.Maui.Navigation;

builder.UseMauiApp<App>().UseCarKit(car => car
    .UseHandler<MyCarAppHandler>()                         // sets a CarMapTemplate as the root
    .UseNavigation(nav => nav.UseHandler<MyNavigationHandler>()));
```

`Navigation` without the package produces the warning `CARKIT008`; the Dashboard and cluster properties without it fail
with `CARKIT009`.

## The navigation session

<xref:CarKit.Maui.ICarNavigationSession> is a singleton: guidance runs on the phone without a car, and every connected car
screen shows it. The states are `Idle` → `Previewing` (`ShowTripPreviewsAsync`) → `Active` (`StartTripAsync`) ⇄ `Paused`
(`PauseAsync`) → `Idle` (`FinishAsync`, `CancelAsync`). During guidance, `UpdateManeuversAsync` (instructions, icons,
lanes, junction images), `UpdateEstimatesAsync`, `ShowAlertAsync` and `RerouteAsync` update CarPlay (`CPNavigationSession`,
`CPMapTemplate`) and Android Auto (`NavigationTemplate`, `NavigationManager.updateTrip`).

<xref:CarKit.Maui.ICarNavigationHandler> receives what the driver and the car do: preview selection, Go/Start, the end
of navigation from the car, maneuvers and alerts selected while the app is in the background (CarPlay), navigation
intents (`geo:`, `google.navigation:`) and auto drive in the DHU (Android Auto). Google requires navigation apps to
handle auto drive (`onAutoDriveEnabled`) for the Play review.

## Map surfaces

`ICarMapSurfaces` has three surfaces: `CarScreen` (CarPlay `CPWindow`, Android Auto `Surface`), `Dashboard` (CarPlay,
iOS 13.4) and `InstrumentCluster` (CarPlay iOS 15.4, Android Auto/AAOS cluster). Each surface takes its content once:

| Method | Content |
| --- | --- |
| `UseRenderer(ICarSurfaceRenderer)` | Your drawing code on `Microsoft.Maui.Graphics` (`Draw(ICanvas, visibleArea, stableArea)`), the same on both platforms |
| `UseViewController(Func<UIViewController>)` (iOS) | A native view controller, for example MapKit, Mapbox or Google Maps |
| `UseView(Func<Context, View>)` (Android) | A native Android view in a `Presentation` on a `VirtualDisplay`; the factory gets the presentation's context |
| `UseMauiView(Func<View>)` | A MAUI view; experimental (`CARKIT0002`), no performance guarantees |

Sizes are in device-independent units (points, dp; `Density` = pixels per unit). `VisibleArea` and `StableArea` follow
the car's safe areas, `ContentStyle` the day/night mode. `Gesture` reports scroll, scale, fling and click; `PanModeChanged`
the pan mode. `FrameStatistics` and `nav.LogFrameTimes` measure frame times (p50, p95, frames over 16.7 and 33.3 ms).

## Voice prompts

`ICarVoicePrompts.SpeakAsync(text)` queues a prompt: `AVSpeechSynthesizer` with ducking of other audio on iOS, and
`TextToSpeech` with transient audio focus (`USAGE_ASSISTANCE_NAVIGATION_GUIDANCE`) on Android. When the car ends
navigation, the queue stops, as Google requires. `nav.VoicePromptLanguage` sets the language (BCP-47).

## iOS 26.4

Route segments with waypoints (`CarRouteChoice.Segments`, `UpdateCurrentSegmentAsync`), the reroute reason and route
sharing with cars that support it (`nav.ShareRoutesWithCar`, requests in `ICarNavigationHandler`) need the iOS 26.4
build and iOS 26.4 ([Platform features](platform-features.md)).

## Background and permissions

- iOS: the build adds `UIBackgroundModes` = `audio` so prompts and maneuvers work with the phone locked. Background
  location (`location`) is the app's decision; CarKit does not add it.
- Android: `NAVIGATION_TEMPLATES` and `ACCESS_SURFACE`. POI and weather apps with the module draw their own map with
  `MapWithContentTemplate` (Car App API 7, `MAP_TEMPLATES` + `ACCESS_SURFACE`).
