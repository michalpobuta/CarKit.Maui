# CarKit.Maui.Core

The platform-neutral model of CarKit.Maui: immutable templates, rows and actions, `ICarAppHandler` and `ICarSession`,
the template stack, the validator with the CarPlay and Android Auto rules, the navigation session model and the
`CarFeature` gate. It targets `net10.0` and has no MAUI dependency, so class libraries, unit tests and app extensions
(for example a SiriKit Intents Extension) can use it.

Apps reference `CarKit.Maui`, which brings this package. Reference `CarKit.Maui.Core` directly from libraries that only
build car screens:

```csharp
public static CarListTemplate Parkings() => new("Parking nearby")
{
    Sections = [new CarSection([new CarListItem("City Center") { Detail = "42 free spots" }])],
};
```

## Documentation

See the documentation site of the source repository (`docs/site`): concepts, templates, validation and the API reference.
