using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Service.Notification;
using AndroidX.Car.App.Messaging.Model;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Serialization;
using AndroidX.Core.App;
using CarKit.Maui.Communication;
using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using static CarKit.Maui.DeviceTests.DeviceConversations;
using NativeCarText = AndroidX.Car.App.Model.CarText;

namespace CarKit.Maui.DeviceTests;

// Stage 6 on the emulator: a ConversationItem from the real renderer and a callback through the host path
// (ConversationCallbackDelegate → AIDL stub → ConversationCallback proxy), a MessagingStyle notification from
// NotificationManager and actions through a PendingIntent with RemoteInput like Android Auto (spec:
// "Communication module decisions (Stage 6)", Testing)
internal static class CommunicationTests
{
    private static readonly Handler _main = new(Looper.MainLooper!);

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Conversation_item_renders_with_messages_and_callback), Conversation_item_renders_with_messages_and_callback),
        new(nameof(New_message_in_a_conversation_is_a_refresh), New_message_in_a_conversation_is_a_refresh),
        new(nameof(Conversation_falls_back_to_row_below_api_7), Conversation_falls_back_to_row_below_api_7),
        new(nameof(Conversation_in_a_sectioned_list_falls_back_to_row), Conversation_in_a_sectioned_list_falls_back_to_row),
        new(nameof(Communication_connections_use_list_template_on_api_8), Communication_connections_use_list_template_on_api_8),
        new(nameof(Conversation_without_messages_falls_back_to_row), Conversation_without_messages_falls_back_to_row),
        new(nameof(Notification_has_messaging_style_and_semantic_actions), Notification_has_messaging_style_and_semantic_actions),
        new(nameof(Reply_intent_reaches_the_handler_and_updates_the_notification), Reply_intent_reaches_the_handler_and_updates_the_notification),
        new(nameof(Mark_as_read_intent_reaches_the_handler_and_cancels), Mark_as_read_intent_reaches_the_handler_and_cancels),
        new(nameof(Two_conversations_keep_separate_actions), Two_conversations_keep_separate_actions),
        new(nameof(Calls_are_not_supported), Calls_are_not_supported),
    ];

    private static IServiceProvider Services => IPlatformApplication.Current!.Services;

    private static DeviceTestMessageHandler Handler => Services.GetRequiredService<DeviceTestMessageHandler>();

    private static CarAndroidMessageNotifier Notifier => Services.GetRequiredService<CarAndroidMessageNotifier>();

    private static Context App => Android.App.Application.Context;

    private static CarAppRenderContext Context(TestSession test, int apiLevel, bool sectioned = false) =>
        new(test.Session, test, apiLevel, App, new CarImageLoader<CarIcon>(new CarAppImages(App), test, combineDarkVariants: false),
            () => CarContentStyle.Light, work => _main.Post(work), sectioned, test);

    private static string? Text(NativeCarText? text) => text?.ToCharSequence();

    private static async Task Conversation_item_renders_with_messages_and_callback()
    {
        var test = TestSession.AndroidAuto(8);
        var calls = new List<string>();
        var conversation = Chat("team", group: true, Message("m1", Anna, "Meeting at 10", read: true), Message("m2", Peter, "I'll be late")) with
        {
            OnReply = c =>
            {
                calls.Add($"reply:{c.Element.Id}:{c.Text}:{MainThread.IsMainThread}");
                return Task.CompletedTask;
            },
            OnMarkAsRead = c =>
            {
                calls.Add($"read:{c.Element.Id}:{MainThread.IsMainThread}");
                return Task.CompletedTask;
            },
        };
        var rendered = CarAppTemplateRenderers.Render(
            new CarListTemplate("Messages") { Sections = [new CarSection([conversation])] }, Context(test, 8), new CarAppScreenState { IsRoot = true });

        var template = Check.Is<ListTemplate>(rendered.Template);
        var item = ((IJavaObject)template.SingleList!.Items![0]).JavaCast<ConversationItem>()!;
        Check.Equal("team", item.Id);
        Check.Equal("Team", Text(item.Title));
        Check.Equal("me", item.Self!.Key);
        Check.Equal("Me", item.Self.Name);
        Check.True(item.IsGroupConversation);
        Check.Equal(2, item.Messages!.Count);
        Check.Equal("anna", item.Messages[0].Sender!.Key);
        Check.Equal("Meeting at 10", Text(item.Messages[0].Body));
        Check.True(item.Messages[0].IsRead);
        Check.True(!item.Messages[1].IsRead);
        Check.Equal(1_761_800_000_000L, item.Messages[1].ReceivedTimeEpochMillis);
        Check.Equal(new CarAppRowShape("Team"), rendered.Shape.Sections[0].Rows[0]);

        // Host path: delegate → IConversationCallback stub → RemoteUtils.dispatchCallFromHost → ConversationCallback proxy
        var replied = new Done();
        item.ConversationCallbackDelegate!.SendTextReply("In 5 minutes", replied);
        await Check.Eventually(() => calls.Contains("reply:team:In 5 minutes:True"), "reply handler");
        await Check.Eventually(() => replied.Succeeded, "reply done callback");
        var read = new Done();
        item.ConversationCallbackDelegate.SendMarkAsRead(read);
        await Check.Eventually(() => calls.Contains("read:team:True"), "mark as read handler");
        await Check.Eventually(() => read.Succeeded, "mark as read done callback");
        Check.Equal(0, test.Errors.Count);
    }

    private static Task New_message_in_a_conversation_is_a_refresh()
    {
        var test = TestSession.AndroidAuto(8);
        var context = Context(test, 8);
        var screen = new CarAppScreenState { IsRoot = true };
        var first = Chat("anna");
        var list = new CarListTemplate("Messages") { Sections = [new CarSection([first])] };

        var before = CarAppTemplateRenderers.Render(list, context, screen);
        var after = CarAppTemplateRenderers.Render(
            list with { Sections = [new CarSection([first with { Messages = first.Messages.Add(Message("m2", Anna, "Hello?")) }])] }, context, screen);

        Check.True(CarAppRefreshRules.IsRefresh(before.Shape, after.Shape));
        Check.Equal(CarAppTemplateTypes.List, after.Shape.Type);
        return Task.CompletedTask;
    }

    private static Task Conversation_falls_back_to_row_below_api_7()
    {
        var test = TestSession.AndroidAuto(6);
        var rendered = CarAppTemplateRenderers.Render(
            new CarListTemplate("Messages") { Sections = [new CarSection([Chat("anna", false, Message("m1", Anna, "Where are you?"))])] },
            Context(test, 6),
            new CarAppScreenState { IsRoot = true });

        var template = Check.Is<ListTemplate>(rendered.Template);
        var row = ((IJavaObject)template.SingleList!.Items![0]).JavaCast<Row>()!;
        Check.Equal("Anna", Text(row.Title));
        Check.Equal("Anna: Where are you?", Text(row.Texts![0]));
        Check.Null(row.OnClickDelegate);
        return Task.CompletedTask;
    }

    // Communication apps do not get SectionedItemTemplate (RowSection accepts only Row); if they did (Warn/Off),
    // a conversation row does not break the renderer
    private static Task Conversation_in_a_sectioned_list_falls_back_to_row()
    {
        Check.True(!CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: null, CarAppCategory.Audio | CarAppCategory.Communication));
        var test = TestSession.AndroidAuto(8);
        var rendered = CarAppTemplateRenderers.Render(
            new CarListTemplate("Messages") { Sections = [new CarSection([Chat("anna")])] }, Context(test, 8, sectioned: true), new CarAppScreenState { IsRoot = true });

        var template = Check.Is<SectionedItemTemplate>(rendered.Template);
        Check.Equal(1, template.Sections!.Count);
        return Task.CompletedTask;
    }

    // Review I7 (Review Focus 3): the host picks the list type of a connection from the app category: Communication →
    // ListTemplate
    private static Task Communication_connections_use_list_template_on_api_8()
    {
        Check.True(!CarAppLibraryHost.UsesSectionedItemTemplate(8, new CarKitOptions { Categories = CarAppCategory.Communication }));
        Check.True(!CarAppLibraryHost.UsesSectionedItemTemplate(8, new CarKitOptions { Categories = CarAppCategory.Audio | CarAppCategory.Communication }));
        Check.True(CarAppLibraryHost.UsesSectionedItemTemplate(8, new CarKitOptions { Categories = CarAppCategory.Parking }));
        Check.True(!CarAppLibraryHost.UsesSectionedItemTemplate(7, new CarKitOptions { Categories = CarAppCategory.Parking }));
        return Task.CompletedTask;
    }

    // Review M4: a conversation without messages (the validator in Warn/Off) does not break the list;
    // ConversationItem.build() would throw
    private static Task Conversation_without_messages_falls_back_to_row()
    {
        var test = TestSession.AndroidAuto(8);
        var rendered = CarAppTemplateRenderers.Render(
            new CarListTemplate("Messages") { Sections = [new CarSection([Chat("empty") with { Messages = [] }, Chat("anna")])] },
            Context(test, 8),
            new CarAppScreenState { IsRoot = true });

        var items = Check.Is<ListTemplate>(rendered.Template).SingleList!.Items!;
        Check.Equal("Anna", Text(((IJavaObject)items[0]).JavaCast<Row>()!.Title));
        Check.Equal("anna", ((IJavaObject)items[1]).JavaCast<ConversationItem>()!.Id);
        return Task.CompletedTask;
    }

    private static StatusBarNotification? Active(string tag) =>
        ((NotificationManager)App.GetSystemService(Android.Content.Context.NotificationService)!).GetActiveNotifications()?.FirstOrDefault(n => n.Tag == tag && n.PackageName == App.PackageName);

    // Stage 7: Android drops app notifications above about 5 per second ("Package enqueue rate is 6.6. Shedding …"
    // in logcat, a moving average); the Media tests just before update the playback notification, so every
    // notification test waits until the average drops
    private static Task AfterNotificationRateLimit() => Task.Delay(TimeSpan.FromSeconds(1.5));

    private static async Task<Notification> Posted(string tag, Func<NotificationCompat.MessagingStyle, bool>? until = null)
    {
        Notification? found = null;
        await Check.Eventually(() =>
        {
            found = Active(tag)?.Notification;
            return found is not null && (until is null || until(NotificationCompat.MessagingStyle.ExtractMessagingStyleFromNotification(found)!));
        }, "notification " + tag);
        return found!;
    }

    private static NotificationCompat.Action Action(Notification notification, int semantic) =>
        NotificationCompat.GetAction(notification, Index(notification, semantic))!;

    // The RemoteInput of an action from the platform action (the AndroidX.Core binding hides getRemoteInputs and
    // getResultKey); the same index
    private static Android.App.RemoteInput[] Inputs(Notification notification, int semantic) =>
        notification.Actions![Index(notification, semantic)].GetRemoteInputs() ?? [];

    private static int Index(Notification notification, int semantic)
    {
        for (var i = 0; i < NotificationCompat.GetActionCount(notification); i++)
        {
            if (NotificationCompat.GetAction(notification, i) is { } action && action.SemanticAction == semantic)
            {
                return i;
            }
        }

        throw new CheckException($"No action with semantic {semantic}");
    }

    private static async Task Notification_has_messaging_style_and_semantic_actions()
    {
        await AfterNotificationRateLimit();
        Check.True(NotificationManagerCompat.From(App)!.AreNotificationsEnabled());
        var conversation = Chat("team-n", group: true, Message("m1", Anna, "Meeting at 10", read: true));

        await Notifier.NotifyAsync(conversation, Message("m2", Peter, "I'll be late"));

        var notification = await Posted("team-n");
        Check.Equal(CarAndroidMessageNotifier.ChannelId, NotificationCompat.GetChannelId(notification));
        Check.Equal(NotificationCompat.CategoryMessage, NotificationCompat.GetCategory(notification));
        var style = Check.NotNull(NotificationCompat.MessagingStyle.ExtractMessagingStyleFromNotification(notification));
        Check.Equal("me", style.User!.Key);
        Check.True(style.IsGroupConversation);
        Check.Equal("Team", style.ConversationTitle);
        Check.Equal(2, style.Messages!.Count);
        Check.Equal("I'll be late", style.Messages[1].Text);
        Check.Equal("peter", style.Messages[1].Person!.Key);

        var reply = Action(notification, NotificationCompat.Action.SemanticActionReply);
        Check.True(!reply.ShowsUserInterface);
        var input = Inputs(notification, NotificationCompat.Action.SemanticActionReply).Single();
        Check.Equal(CarAndroidMessageNotifier.ReplyKey, input.ResultKey);
        Check.True(input.AllowFreeFormInput);
        Check.Equal("Reply", reply.Title);
        var read = Action(notification, NotificationCompat.Action.SemanticActionMarkAsRead);
        Check.True(!read.ShowsUserInterface);
        Check.Equal(0, Inputs(notification, NotificationCompat.Action.SemanticActionMarkAsRead).Length);
        Check.Equal("Mark as read", read.Title);
        await Notifier.CancelAsync("team-n");
        await Check.Eventually(() => Active("team-n") is null, "cancelled notification");
    }

    // Android Auto: a voice reply reaches the PendingIntent of the Reply action as a RemoteInput result (Review Focus 1)
    private static async Task Reply_intent_reaches_the_handler_and_updates_the_notification()
    {
        await AfterNotificationRateLimit();
        await Notifier.NotifyAsync(Chat("anna-r"), Message("m2", Anna, "Where are you?"));
        var notification = await Posted("anna-r");
        var reply = Action(notification, NotificationCompat.Action.SemanticActionReply);
        var fill = new Intent();
        var results = new Bundle();
        results.PutCharSequence(CarAndroidMessageNotifier.ReplyKey, new Java.Lang.String("In 5 minutes"));
        Android.App.RemoteInput.AddResultsToIntent(Inputs(notification, NotificationCompat.Action.SemanticActionReply), fill, results);

        reply.ActionIntent!.Send(App, 0, fill);

        await Check.Eventually(() => Handler.Calls.Contains("reply:anna-r:In 5 minutes"), "reply handler on the main thread");
        // The reply is appended to the notification as a user message (without an action spinner)
        var updated = await Posted("anna-r", style => style.Messages!.Count == 3);
        var style = NotificationCompat.MessagingStyle.ExtractMessagingStyleFromNotification(updated)!;
        Check.Equal("In 5 minutes", style.Messages![2].Text);
        Check.Null(style.Messages[2].Person);
        await Notifier.CancelAsync("anna-r");
    }

    private static async Task Mark_as_read_intent_reaches_the_handler_and_cancels()
    {
        await AfterNotificationRateLimit();
        await Notifier.NotifyAsync(Chat("anna-m"), Message("m2", Anna, "Call me back"));
        var notification = await Posted("anna-m");

        Action(notification, NotificationCompat.Action.SemanticActionMarkAsRead).ActionIntent!.Send();

        await Check.Eventually(() => Handler.Calls.Contains("read:anna-m"), "mark as read handler");
        await Check.Eventually(() => Active("anna-m") is null, "notification removed after mark as read");
    }

    // Review Focus 2: the PendingIntents of two conversations do not overwrite each other (requestCode and URI data)
    private static async Task Two_conversations_keep_separate_actions()
    {
        await AfterNotificationRateLimit();
        await Notifier.NotifyAsync(Chat("chat-a"), Message("a1", Anna, "A"));
        await Notifier.NotifyAsync(Chat("chat-b"), Message("b1", Peter, "B"));
        var first = await Posted("chat-a");
        await Posted("chat-b");

        Action(first, NotificationCompat.Action.SemanticActionMarkAsRead).ActionIntent!.Send();

        await Check.Eventually(() => Handler.Calls.Contains("read:chat-a"), "first conversation marked");
        await Check.Eventually(() => Active("chat-a") is null, "first notification removed");
        Check.True(!Handler.Calls.Contains("read:chat-b"));
        Check.NotNull(Active("chat-b"));
        await Notifier.CancelAsync("chat-b");
    }

    // The Android calls gate (no Core-Telecom binding)
    private static async Task Calls_are_not_supported()
    {
        var calls = Services.GetRequiredService<ICarCallManager>();

        Check.True(!calls.IsSupported);
        try
        {
            await calls.ReportIncomingCallAsync(new CarCallHandle(CarCallHandleType.PhoneNumber, "+15550100200"));
            throw new CheckException("Expected PlatformNotSupportedException");
        }
        catch (PlatformNotSupportedException ex)
        {
            Check.True(ex.Message.Contains("Core-Telecom", StringComparison.Ordinal));
        }
    }

    private sealed class Done : Java.Lang.Object, AndroidX.Car.App.IOnDoneCallback
    {
        public bool Succeeded { get; private set; }

        public void OnFailure(Bundleable? response)
        {
        }

        public void OnSuccess(Bundleable? response) => Succeeded = true;
    }
}
