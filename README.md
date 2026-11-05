# CarKit.Maui

Apple CarPlay and Android Auto for .NET MAUI apps, written in C# on the official .NET bindings. Describe the car screens
once with immutable C# records; CarKit renders them on both platforms, validates them against each platform's rules
before the car sees them, and generates `Info.plist`, entitlements and `AndroidManifest.xml` entries from one property
in your project file.

**Status: beta.** The API can change before 1.0, and not every scenario has been tested in a real car yet. See
[known limitations](docs/site/articles/known-limitations.md).

## Install

```bash
dotnet add package CarKit.Maui --prerelease
```

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
  <CarKitCategories>Parking</CarKitCategories>
</PropertyGroup>
```

```csharp
using CarKit.Maui;

builder.UseMauiApp<App>().UseCarKit(car => car.UseHandler<ParkingCarAppHandler>());

public sealed class ParkingCarAppHandler : ICarAppHandler
{
    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) =>
        session.SetRootAsync(new CarListTemplate("Parking nearby")
        {
            Sections = [new CarSection([new CarListItem("City Center") { Detail = "42 free spots" }])],
        });
}
```

## Packages

| Package | Use it for |
| --- | --- |
| `CarKit.Maui` | Every app: templates, CarPlay scene, Android Auto `CarAppService`, `UseCarKit()`, MSBuild targets |
| `CarKit.Maui.Core` | The platform-neutral model (`net10.0`); comes with `CarKit.Maui` |
| `CarKit.Maui.Media` | Audio apps: media library, Now Playing, Media3 service |
| `CarKit.Maui.Communication` | Messaging and calling: conversations, notifications, Siri, CallKit |
| `CarKit.Maui.Navigation` | Navigation apps: map surfaces, turn-by-turn session, voice prompts, Dashboard and cluster |
| `CarKit.Maui.Testing` | Unit tests with `FakeCarHost` (`net10.0`) |

## Requirements

- .NET 10 SDK (10.0.2xx) with the `maui-ios` and `maui-android` workloads, .NET MAUI 10.0.120 or later.
- iOS 15+ (build with Xcode 26.4), Android 6.0 (API 23)+, Android Auto hosts from Car App API level 1.
- A CarPlay entitlement from Apple for device builds; Google's review for Android Auto in the Play Store.

## Documentation

- [Your first CarPlay & Android Auto app in 15 minutes](docs/site/articles/quickstart.md)
- [Documentation site sources](docs/site/index.md) — build it with `eng/docs/build.sh` (DocFX); concepts, modules,
  entitlements and store approval, diagnostics, migration, known limitations and the API reference.
- [CHANGELOG](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md): building the repository, tests, device tests and releases.
- [Security policy](SECURITY.md) and [code of conduct](CODE_OF_CONDUCT.md).
- [Specification](docs/specification.md) — the design document and stage plan of the project.

## License

CarKit.Maui is released under the [MIT license](LICENSE).

Apple CarPlay and Android Auto are trademarks of their respective owners. This project is not affiliated with, endorsed
by or sponsored by Apple or Google.
