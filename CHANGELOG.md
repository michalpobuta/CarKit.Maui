# Changelog

All notable changes to CarKit.Maui are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the packages follow [Semantic Versioning](https://semver.org/).

## 0.1.0-beta.1 - Unreleased

The first public beta: Apple CarPlay and Android Auto for .NET MAUI apps in six NuGet packages, under the MIT license.
The API can change before 1.0.

### Core (`CarKit.Maui.Core`, `CarKit.Maui`)

- Immutable template model with stable identifiers and structural equality (`CarArray<T>`), updated with `with` and
  `ICarSession.UpdateAsync`; CarPlay objects are changed in place, Android Auto templates are refreshed without using a
  task step where the host allows it.
- `ICarAppHandler` and `ICarSession` with an imperative stack: `SetRootAsync`, `PushAsync`, `PopAsync`, `PopToAsync`,
  `PopToRootAsync`, `PresentAsync`, `DismissAsync`; car sessions start without the phone UI (locked iPhone, no Android
  `Activity`).
- 16 template types: list (with `SectionedItemTemplate` on Car App API 8), grid, tabs, information, search, point of
  interest, alert, message, action sheet, long message, voice control, Now Playing, map, `CarPlayContactTemplate`,
  `AndroidAutoSignInTemplate` and the experimental `CarPlayVideoBrowseTemplate`; built-in and explicit fallbacks.
- Validator with the CarPlay and Android Auto rules as data: allowed templates per category, stack depth, item and alert
  action limits read from the platform, Android Auto task steps; `Strict`, `Warn` and `Off` modes.
- All app categories: audio, communication, navigation, parking, EV charging, fueling, quick food ordering, driving task,
  voice-based conversation, video, public safety, IoT and weather.
- Image pipeline: files, resources, SF Symbols and app drawables, bytes, URIs and streams, dark variants and tints, caching.
- Escape hatch to the native objects: `ConfigureCarPlay` and `ConfigureAndroidAuto`.
- MSBuild targets: scene manifest, entitlements, background modes and usage descriptions on iOS; generated
  `CarAppService`, intent filters, permissions and `automotive_app_desc.xml` on Android; diagnostics `CARKIT001`–`CARKIT009`.
- Platform features behind `CarFeature` and `ICarCapabilities.Supports`: voice control action buttons, list details
  header, item playback state and route sharing (iOS 26.4), Now Playing sports mode (iOS 18.4). The iOS packages ship an
  iOS 26.0 and an iOS 26.4 build.

### Media (`CarKit.Maui.Media`)

- `ICarMediaLibrary` and `ICarMediaPlayer` with automatic CarPlay browsing, Now Playing (`MPNowPlayingInfoCenter`,
  `MPRemoteCommandCenter`), a generated Media3 `MediaLibraryService` for Android Auto, audio focus, interruptions and
  ducking; optional Android Auto media templates.

### Communication (`CarKit.Maui.Communication`)

- Conversation rows, `MessagingStyle` notifications with reply and mark-as-read, a CarPlay notification category, Siri
  through an Intents Extension and an App Group store, VoIP calls with CallKit.

### Navigation (`CarKit.Maui.Navigation`)

- Map surfaces on the car screen, the CarPlay Dashboard and instrument clusters (custom drawing, native views, MAUI views
  as an experiment), one navigation session for the phone and the cars (previews, maneuvers with lanes, estimates,
  alerts, reroutes, pauses, route segments), voice prompts with ducking, gestures and frame statistics.

### Testing (`CarKit.Maui.Testing`)

- `FakeCarHost`: run handlers against a simulated CarPlay or Android Auto host with the same rules as the renderers.

### Requirements

- .NET 10 SDK (10.0.2xx) with the MAUI workloads; .NET MAUI 10.0.120 or later.
- iOS 15.0 or later; apps targeting `net10.0-ios` get the iOS 26.4 build (Xcode 26.4), apps targeting `net10.0-ios26.0`
  and class libraries targeting `net10.0-ios` get the iOS 26.0 build.
- Android 6.0 (API level 23) or later; Android for Cars App Library 1.7 (`Xamarin.AndroidX.Car.App.App` 1.7.0.1), Car App
  API levels 1–8; Media3 1.9.0 for the media module.

### Known limitations

- iOS 27 features (MiniPlayer opt-out, voice control overlay and others) are not available: the iOS 27 SDK needs
  Xcode 27. `AllowsMiniPlayer` and `IsOverlay` are experimental models without effect, and the experimental
  `CarPlayVideoBrowseTemplate` renders as a list.
- No support for Car App Library 1.8 and 1.9 (no .NET binding yet).
- No calls in Android Auto (no .NET binding of Jetpack Core-Telecom) and no Android Automotive OS target.
- Android Auto media templates and templated messaging can only be published to testing tracks (Google's programs).
- Audio apps show one alert action on CarPlay (platform limit of the audio entitlement); CarKit keeps the first action.

See the [known limitations](docs/site/articles/known-limitations.md), the
[quickstart](docs/site/articles/quickstart.md) and the [entitlement process](docs/site/articles/entitlements.md).

