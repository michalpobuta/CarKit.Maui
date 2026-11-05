# CarKit.Maui.Testing

`FakeCarHost` runs your `ICarAppHandler` against a simulated CarPlay or Android Auto host in a plain `net10.0` unit test:
it applies the same validator and platform rules as the real renderers, records every call, and lets the test act as the
driver (select rows, press back, change the theme, disconnect).

```csharp
using var car = new FakeCarHost(CarAppCategory.Parking, CarPlatform.AndroidAuto);
var session = await car.ConnectAsync(new MyCarAppHandler());

var list = Assert.IsType<CarListTemplate>(session.Stack[^1]);
await car.SelectAsync(list.Sections[0].Items[0]);
Assert.IsType<CarInformationTemplate>(session.Stack[^1]);
```

## Documentation

See the "Testing" page of the documentation site in the source repository (`docs/site`).
