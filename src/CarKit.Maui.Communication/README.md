# CarKit.Maui.Communication

Messaging and calling for CarPlay and Android Auto (category `Communication`): conversation rows in lists
(`CPMessageListItem`, `ConversationItem`), message notifications with reply and mark-as-read (`MessagingStyle` for
Android Auto, a CarPlay notification category), Siri through your Intents Extension and a shared App Group store, and
VoIP calls through CallKit.

```xml
<CarKitCategories>Communication</CarKitCategories>
<CarKitIosAppGroup>group.com.example.messages</CarKitIosAppGroup>
```

```csharp
using CarKit.Maui;
using CarKit.Maui.Communication;

builder.UseMauiApp<App>().UseCarKit(car => car
    .UseHandler<MyCarAppHandler>()
    .UseCommunication(c => c.UseMessageHandler<MyMessages>().UseCallHandler<MyCalls>()));
```

Calls in Android Auto wait for a .NET binding of Jetpack Core-Telecom: `ICarCallManager.IsSupported` is `false` on
Android and `<CarKitAndroidCalling>true</CarKitAndroidCalling>` fails the build with `CARKIT004`.

## Documentation

See the "Communication" page of the documentation site in the source repository (`docs/site`).
