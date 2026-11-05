# CarKit.Maui.Navigation

Turn-by-turn navigation for CarPlay and Android Auto (category `Navigation`): the map template over a surface drawn by
your app (`Microsoft.Maui.Graphics`, a native map view or, experimentally, a MAUI view), one navigation session for the
phone and every car screen (route previews, maneuvers with lanes, estimates, alerts, reroutes, pauses), voice prompts with
audio ducking, the CarPlay Dashboard and instrument cluster, and the Android Auto cluster.

```xml
<CarKitCategories>Navigation</CarKitCategories>
<CarKitIosDashboard>true</CarKitIosDashboard>
```

```csharp
using CarKit.Maui;
using CarKit.Maui.Navigation;

builder.UseMauiApp<App>().UseCarKit(car => car
    .UseHandler<MyCarAppHandler>()
    .UseNavigation(nav => nav.UseHandler<MyNavigationHandler>()));
```

The package draws no map tiles itself: the app renders the map with its map SDK or its own drawing code.

## Documentation

See the "Navigation" page of the documentation site in the source repository (`docs/site`).
