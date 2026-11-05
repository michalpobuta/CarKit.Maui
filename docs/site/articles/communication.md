# Communication

`CarKit.Maui.Communication` adds messaging and calling (category `Communication`). The car screens stay with your
`ICarAppHandler`; the module adds notifications, Siri and CallKit.

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

`Communication` without the package produces the warning `CARKIT007`.

## Conversations in lists

`CarConversationItem` is a row of `CarSection`: `CPMessageListItem` on CarPlay (selecting it hands over to Siri) and
`ConversationItem` on Android Auto from Car App API 7 (reply and mark-as-read call the item's `OnReply` and
`OnMarkAsRead`; below API 7 a plain row with the last message). Conversation rows are allowed only in the
`Communication` category.

## Message notifications

`ICarMessageNotifier.NotifyAsync(conversation, message)` posts a message notification:

- Android: a `MessagingStyle` notification with the semantic Reply (`RemoteInput`) and Mark as read actions that Android
  Auto requires. The generated receiver calls `ICarMessageHandler.OnReplyAsync`/`OnMarkAsReadAsync` and updates the
  notification.
- iOS: a notification category with `AllowInCarPlay`, Reply and Mark as read. `RequestPermissionAsync` asks for alerts
  with the CarPlay option and, when the App Group store works, for Siri.

CarKit installs its own `UNUserNotificationCenter` delegate and forwards other notifications to the previous delegate.
A delegate the app sets later replaces CarKit's, and the CarKit notification actions stop working.

## Siri (iOS)

Reading and replying in CarPlay go through SiriKit: `INSearchForMessagesIntent` and `INSendMessageIntent` in an Intents
Extension of the app. The extension cannot load MAUI, so it uses `CarKit.Maui.Core` only: the app publishes conversations
to `CarConversationStore` in the App Group container (`<CarKitIosAppGroup>`), the extension reads them and queues reply
and mark-as-read requests, and the app processes the requests at start, after the extension's Darwin notification and
when it returns to the foreground. A suspended app cannot process them, so a real messenger sends the reply from the
extension and uses the store to keep the app's state in sync. The build adds the Siri and App Group entitlements (device
builds) and `NSSiriUsageDescription`.

## Calls

- iOS: `ICarCallManager` on CallKit (`CXProvider`, `CXCallController`): report incoming calls, start outgoing ones, end,
  hold and mute; system actions from CarPlay, the lock screen and Siri reach `ICarCallHandler`. CarPlay shows the call in
  its Phone app.
- Android Auto: calls need the Jetpack Core-Telecom library, which has no .NET binding yet. `ICarCallManager.IsSupported`
  is `false` on Android, its methods throw `PlatformNotSupportedException`, the `CALLING` category is not declared, and
  `<CarKitAndroidCalling>true</CarKitAndroidCalling>` fails the build with `CARKIT004`.

## Store restrictions

Google lists "templated messaging" (conversations in Car App Library templates) and "calling" as labs categories that can
only be published to internal and closed testing tracks; messaging notifications can use every track. See
[Entitlements and store approval](entitlements.md#google-play).

## Options

| Option | Default |
| --- | --- |
| `NotificationChannelName` | "Messages" |
| `ReplyActionTitle` | "Reply" |
| `MarkAsReadActionTitle` | "Mark as read" |
| `AndroidSmallIcon` | The app icon |
| `IncludeCallsInRecents` | `true` |
