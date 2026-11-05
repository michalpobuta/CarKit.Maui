---
_layout: landing
---

# CarKit.Maui

CarKit.Maui brings Apple CarPlay and Android Auto to .NET MAUI apps. You describe car screens once with immutable C#
records; CarKit renders them with the official .NET bindings (`CarPlay` on iOS, the Android for Cars App Library and
Media3 on Android), checks them against each platform's rules before the car sees them, and generates `Info.plist`,
entitlements and `AndroidManifest.xml` entries from one property in your project file.

```csharp
builder.UseMauiApp<App>().UseCarKit(car => car.UseHandler<ParkingCarAppHandler>());
```

```xml
<CarKitCategories>Parking</CarKitCategories>
```

## Start here

- [Your first CarPlay & Android Auto app in 15 minutes](articles/quickstart.md)
- [Installation and requirements](articles/getting-started.md)
- [Concepts: sessions, the template stack and updates](articles/concepts.md)
- [Entitlements and store approval](articles/entitlements.md)
- [API reference](api/index.md)

## Packages

| Package | Targets | Contents |
| --- | --- | --- |
| `CarKit.Maui` | `net10.0-ios`, `net10.0-android` | Renderers, hosting (CarPlay scene, `CarAppService`), `UseCarKit`, MSBuild targets |
| `CarKit.Maui.Core` | `net10.0` | Template model, session, stack, validator; no MAUI dependency |
| `CarKit.Maui.Media` | `net10.0-ios`, `net10.0-android` | Media library, Now Playing, `MPNowPlayingInfoCenter` and Media3 bridges |
| `CarKit.Maui.Communication` | `net10.0-ios`, `net10.0-android` | Conversations, message notifications, Siri, CallKit |
| `CarKit.Maui.Navigation` | `net10.0-ios`, `net10.0-android` | Map surfaces, turn-by-turn navigation session, voice prompts, Dashboard and instrument cluster |
| `CarKit.Maui.Testing` | `net10.0` | `FakeCarHost` for unit tests without a simulator |

## What is covered

- 16 template types: shared templates (list, grid, tabs, information, search, alert, message, action sheet, long
  message, point of interest, map, Now Playing, voice control) and platform templates (`CarPlayContactTemplate`,
  `AndroidAutoSignInTemplate`, `CarPlayVideoBrowseTemplate`), with fallbacks and access to the native objects.
- Every app category of both platforms: audio, communication, navigation, parking, EV charging, fueling, quick food
  ordering, driving task, voice-based conversation, video, public safety, IoT and weather.
- Platform features up to iOS 26.4 and Car App API level 8 behind one feature gate (`CarFeature`).

See [Known limitations](articles/known-limitations.md) for what is not available yet.
