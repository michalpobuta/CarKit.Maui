# Installation and requirements

## Requirements

| Area | Requirement |
| --- | --- |
| .NET | .NET 10 SDK, band 10.0.2xx, with the `maui-ios` and `maui-android` workloads. CarKit 0.1 is built and tested with SDK 10.0.201 and workload set 10.0.204.1 |
| MAUI | `Microsoft.Maui.Controls` 10.0.120 or later (CarKit depends on it) |
| iOS | iOS 15.0 or later on the device. Building needs Xcode 26.4 for apps that target `net10.0-ios` (the workload's iOS 26.4 SDK); Xcode 26.0 is enough for apps that target `net10.0-ios26.0` |
| Android | Android 6.0 (API level 23) or later, API 26+ recommended (Android Auto itself needs Android 9); Android Auto hosts from Car App API level 1. Newer templates are used when the host reports a higher Car App API level |
| Tools | macOS for iOS builds; an iOS simulator for CarPlay (**I/O → External Displays → CarPlay**); the Android Auto Desktop Head Unit (DHU) for Android Auto |
| Store | A CarPlay entitlement from Apple for device, TestFlight and App Store builds; the Android Auto form factor and Google's review in the Play Console. See [Entitlements and store approval](entitlements.md) |

## Install the packages

Add `CarKit.Maui` to the MAUI app. Add a module package when the app uses its category:

```bash
dotnet add package CarKit.Maui --prerelease
dotnet add package CarKit.Maui.Media --prerelease          # Audio
dotnet add package CarKit.Maui.Communication --prerelease  # Communication
dotnet add package CarKit.Maui.Navigation --prerelease     # Navigation, POI or weather with an app-drawn map
```

A module package brings `CarKit.Maui` with it, and every CarKit package depends on the exact same version of the other
CarKit packages. Unit test projects reference `CarKit.Maui.Testing` (`net10.0`), which brings `CarKit.Maui.Core`. Class
libraries that only build car screens can reference `CarKit.Maui.Core` alone.

The app targets `net10.0-ios` and `net10.0-android`; CarKit has no Mac Catalyst or Windows build. The library ships two
iOS builds, picked by NuGet: apps get the iOS 26.4 build, libraries that target plain `net10.0-ios` get the iOS 26.0
build ([Platform features](platform-features.md#two-ios-builds)).

## Configure the project

Declare the car app categories once, in the app's `.csproj`. CarKit's MSBuild targets generate everything the platforms
need from it ([Categories and project configuration](categories.md)):

```xml
<PropertyGroup>
  <CarKitCategories>Audio;Parking</CarKitCategories>
</PropertyGroup>
```

- iOS: the CarPlay scene in the scene manifest (`Info.plist`), the CarPlay entitlement of each category, background
  audio and usage descriptions where a category needs them.
- Android: a generated `CarAppService` with the category intent filters, the template permissions,
  `automotive_app_desc.xml`, and the Media3 service or notification receiver of the modules.

## Register CarKit

Call `UseCarKit` on the `MauiAppBuilder` and register the handler that owns the car screens:

```csharp
builder
    .UseMauiApp<App>()
    .UseCarKit(car =>
    {
        car.UseHandler<MyCarAppHandler>();                     // singleton in the app's services
        car.UnsupportedTemplateBehavior = UnsupportedTemplateBehavior.UseFallbackOrThrow;
        car.Limits.OnOverflow = CarOverflowBehavior.Truncate;  // or Throw
        car.Diagnostics.LogNativeCalls = true;                 // log every native call (Debug)
        car.Android.AllowedHosts.Add("<sha256>,com.example.host");
    });
```

| Option | Default | Meaning |
| --- | --- | --- |
| `car.Categories` | From `<CarKitCategories>` | Read-only in practice: a different value fails `UseCarKit` |
| `car.UseHandler<T>()` | — | Required, except for media apps that use `media.UseAutoBrowse()` |
| `car.Validation` | `Strict`; `Warn` in `Release` builds | See [Validation and limits](validation.md) |
| `car.Limits.OnOverflow` | `Truncate` | What happens to items above a platform limit |
| `car.UnsupportedTemplateBehavior` | `UseFallbackOrThrow` | Templates one platform does not have ([Concepts](concepts.md#templates-of-one-platform)) |
| `car.Android.AllowedHosts` | CarKit's list of Android Auto hosts | Release builds only; Debug builds accept every host so the DHU works |

Exceptions thrown by your callbacks never crash the car session: CarKit passes them to `ICarErrorHandler`, which logs
them with the category `CarKit.Session` by default. Register your own `ICarErrorHandler` in `builder.Services` before
`UseCarKit` to report them elsewhere.

## Next steps

Follow [Your first CarPlay & Android Auto app in 15 minutes](quickstart.md), then read [Concepts](concepts.md).
