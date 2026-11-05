# CarKit.Maui

Apple CarPlay and Android Auto for .NET MAUI apps: renderers for every CarKit template, hosting (the CarPlay scene and
the Android Auto `CarAppService`), `UseCarKit()` for `MauiAppBuilder`, and MSBuild targets that generate `Info.plist`,
entitlements and `AndroidManifest.xml` entries from one project property.

## Getting started

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
  <CarKitCategories>Parking</CarKitCategories>
</PropertyGroup>
```

```csharp
using CarKit.Maui;

builder.UseMauiApp<App>().UseCarKit(car => car.UseHandler<MyCarAppHandler>());

public sealed class MyCarAppHandler : ICarAppHandler
{
    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) =>
        session.SetRootAsync(new CarListTemplate("Parking nearby")
        {
            Sections = [new CarSection([new CarListItem("City Center") { Detail = "42 free spots" }])],
        });
}
```

## Requirements

.NET 10 SDK (10.0.2xx) with the MAUI workloads, `Microsoft.Maui.Controls` 10.0.120 or later, iOS 15+ (build with
Xcode 26.4), Android API 23+. Device builds for CarPlay need Apple's CarPlay entitlement for the app's category.

## Documentation

The guide, concepts, the entitlement process and the API reference are in the documentation site of the source
repository (`docs/site`); start with "Your first CarPlay & Android Auto app in 15 minutes".
