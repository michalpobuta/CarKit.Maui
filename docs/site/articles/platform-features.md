# Platform features

CarPlay and Android Auto add features every year. CarKit exposes them in the shared model and decides per connection
whether the car, the operating system and the CarKit build can render them.

## Two iOS builds

The iOS packages (`CarKit.Maui`, `CarKit.Maui.Media`, `CarKit.Maui.Communication`, `CarKit.Maui.Navigation`) contain two
builds, and NuGet picks one:

| Your project | CarKit build it gets | Xcode needed to build |
| --- | --- | --- |
| App with `net10.0-ios` | `net10.0-ios26.4` (iOS 26.4 SDK): the workload builds apps with the iOS 26.4 SDK | Xcode 26.4 |
| App with `net10.0-ios26.4` | `net10.0-ios26.4` | Xcode 26.4 |
| App with `net10.0-ios26.0` | `net10.0-ios` (iOS 26.0 SDK) | Xcode 26.0 |
| Class library with `net10.0-ios` | `net10.0-ios` (iOS 26.0 SDK) | — |

The iOS 26.4 build has the same API plus the iOS 26.4 features below; the iOS 26.0 build renders the same models without
them. Both run on iOS 15 and later: features of a newer iOS are used only when the device runs it.

## The feature gate

`session.Capabilities.Supports(CarFeature.X)` is `true` only when all three hold: the platform version from CarKit's
rules (iOS version, Car App API level), the CarKit build that renders the feature, and the car (for video, the car must
report video playback).

```csharp
var list = session.Capabilities.Supports(CarFeature.ListDetailsHeader)
    ? new CarListTemplate("Parking") { DetailsHeader = new CarListDetailsHeader(thumbnail) { Title = "City Center" }, Sections = [nearby] }
    : new CarListTemplate("Parking") { Sections = [featured, nearby] };
```

You do not have to ask: before a template reaches the car, CarKit removes the properties the car cannot render (for
example `DetailsHeader` becomes `null`, voice control actions become empty) in every validation mode and logs it at
`Information` level. `session.Stack` keeps the model you sent.

| `CarFeature` | Platform | Model |
| --- | --- | --- |
| `VoiceControlActionButtons` | CarPlay, iOS 26.4 build, iOS 26.4 | `CarVoiceControlState.Actions` (up to 2 buttons); Android Auto shows them as the actions of the fallback message |
| `ListDetailsHeader` | CarPlay, iOS 26.4 build, iOS 26.4 | `CarListTemplate.DetailsHeader` |
| `PlaybackConfiguration` | CarPlay, iOS 26.4 build, iOS 26.4 | `CarListItem.Playback`, `CarListDetailsHeader.Playback` |
| `NowPlayingSportsMode` | CarPlay, iOS 18.4 (both builds) | `CarNowPlayingTemplate.Sports`, `CarMediaItem.Sports` |
| `RouteSharing` | CarPlay, iOS 26.4 build, iOS 26.4 | Route segments, waypoints, reroute reasons, `nav.ShareRoutesWithCar` |
| `VideoPlayback` | CarPlay, iOS 26.4 build, a car with video | `CarPlayVideoBrowseTemplate` rows with video playback state |
| `NowPlayingMiniPlayerOptOut` | iOS 27 — not available in CarKit 0.1 | `CarNowPlayingTemplate.AllowsMiniPlayer` (experimental) |
| `VoiceControlOverlay` | iOS 27 — not available in CarKit 0.1 | `CarVoiceControlTemplate.IsOverlay` (experimental) |

In unit tests, `FakeCarHost.Features` simulates the CarKit build and the car: it contains every feature except the iOS 27
ones by default; remove features to test the fallback ([Testing](testing.md)).

## iOS 27

The iOS 27 APIs need the iOS 27 SDK, which needs Xcode 27; CarKit 0.1 is built without it. The models that describe iOS
27 features exist and are marked experimental (`CARKIT0001`): `CarPlayVideoBrowseTemplate`, `AllowsMiniPlayer` and
`IsOverlay`. On current builds they have no effect beyond what the table says. A future CarKit release adds an
`net10.0-ios27.0` build. See [Known limitations](known-limitations.md).

## Android Auto

CarKit uses the Car App Library 1.7 binding (`Xamarin.AndroidX.Car.App.App` 1.7.0.1) up to Car App API level 8:
`SectionedItemTemplate` (lists on API 8), `MediaPlaybackTemplate` (API 8), `MapWithContentTemplate` and conversations
(API 7), tabs (API 6), alerts (API 5). Car App Library 1.8 and 1.9 (API level 9: search header, banner and chip
sections, tab end action) have no .NET binding yet, so CarKit has no code for them; `CarKitAndroidMinCarApiLevel` above
8 fails with `CARKIT002`.
