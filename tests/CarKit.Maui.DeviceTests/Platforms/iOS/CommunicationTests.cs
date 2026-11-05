using CallKit;
using CarKit.Maui.Communication;
using CarKit.Maui.Renderers;
using CarKit.Sample.Communication.Intents;
using CarPlay;
using Foundation;
using Intents;
using Microsoft.Extensions.DependencyInjection;
using UIKit;
using UserNotifications;
using static CarKit.Maui.DeviceTests.DeviceConversations;

namespace CarKit.Maui.DeviceTests;

// Stage 6 on the simulator: a CPMessageListItem from the real renderer, the CarKit notification category
// (AllowInCarPlay), the sample Intents Extension handler on a store in a temporary directory, and CallKit (spec:
// "Communication module decisions (Stage 6)")
internal static class CommunicationTests
{
    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Conversation_rows_render_as_message_list_items), Conversation_rows_render_as_message_list_items),
        new(nameof(Conversation_rows_update_in_place), Conversation_rows_update_in_place),
        new(nameof(Message_category_allows_car_play), Message_category_allows_car_play),
        new(nameof(Notification_request_carries_the_conversation), Notification_request_carries_the_conversation),
        new(nameof(Intents_handler_searches_and_replies_through_the_store), Intents_handler_searches_and_replies_through_the_store),
        new(nameof(CallKit_reports_and_ends_an_incoming_call), CallKit_reports_and_ends_an_incoming_call),
        new(nameof(Concurrent_store_writes_keep_every_conversation), Concurrent_store_writes_keep_every_conversation),
        new(nameof(Darwin_notification_drains_store_requests), Darwin_notification_drains_store_requests),
        new(nameof(Notify_restores_a_category_removed_by_the_app), Notify_restores_a_category_removed_by_the_app),
    ];

    private static async Task WithStore(Func<CarConversationStore, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "carkit-bridge-" + Guid.NewGuid().ToString("N"));
        try
        {
            await test(new CarConversationStore(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // Review I3: parallel NotifyAsync calls for different conversations do not lose conversations in the Siri store
    // (read-merge-write)
    private static Task Concurrent_store_writes_keep_every_conversation() => WithStore(async store =>
    {
        var bridge = new CarSiriBridge(Services.GetRequiredService<CarCommunicationRuntime>());
        bridge.UseStore(store, appGroup: null);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => bridge.SaveConversationAsync(Chat("c" + i)))));

            Check.Equal(20, (await store.LoadConversationsAsync()).Count);
        }
        finally
        {
            bridge.Stop();
        }
    });

    // Review I7: a request from the Intents Extension + a Darwin notification → the running app's ICarMessageHandler
    private static Task Darwin_notification_drains_store_requests() => WithStore(async store =>
    {
        const string appGroup = "group.com.carkit.devicetests";
        var bridge = new CarSiriBridge(Services.GetRequiredService<CarCommunicationRuntime>());
        bridge.UseStore(store, appGroup);
        try
        {
            await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, "darwin") { Text = "Hey" });
            CoreFoundation.CFNotificationCenter.Darwin.PostNotification(CarConversationStore.GetRequestsNotificationName(appGroup), null!, null!, true, true);

            await Check.Eventually(() => Services.GetRequiredService<DeviceTestMessageHandler>().Calls.Contains("reply:darwin:Hey"), "request drained after the Darwin notification");
        }
        finally
        {
            bridge.Stop();
        }
    });

    // Review I4: the app overwrites the categories (SetNotificationCategories); NotifyAsync restores carkit.message
    // and keeps the app's categories (on a simulator without notification consent NotifyAsync ends after the category)
    private static async Task Notify_restores_a_category_removed_by_the_app()
    {
        var center = UNUserNotificationCenter.Current;
        var app = UNNotificationCategory.FromIdentifier("app.other", [], [], UNNotificationCategoryOptions.None);
        center.SetNotificationCategories(new NSSet<UNNotificationCategory>(app));
        await Check.Eventually(() => !Ids(center).Contains(CarIosMessageNotifier.CategoryId), "category removed by the app");

        await Services.GetRequiredService<ICarMessageNotifier>().NotifyAsync(Chat("restore"), Message("r1", Anna, "Hello"));

        await Check.Eventually(() => Ids(center).Contains(CarIosMessageNotifier.CategoryId) && Ids(center).Contains("app.other"), "carkit.message restored next to app.other");
    }

    private static string[] Ids(UNUserNotificationCenter center) =>
        [.. center.GetNotificationCategoriesAsync().GetAwaiter().GetResult().ToArray().Select(c => c.Identifier)];

    private static IServiceProvider Services => IPlatformApplication.Current!.Services;

    private static CarPlayRenderContext Context(TestSession test) =>
        new(test.Session, test, new CarImageLoader<UIImage>(new CarPlayImages(2), test, combineDarkVariants: true), 2, () => CarContentStyle.Light, _ => Task.CompletedTask);

    private static CPMessageListItem Item(CPListTemplate list, int index) => list.Sections[0].Item<CPMessageListItem>(index);

    private static Task Conversation_rows_render_as_message_list_items()
    {
        var test = TestSession.CarPlay(CarAppCategory.Communication);
        var list = new CarListTemplate("Messages")
        {
            Sections =
            [
                new CarSection([
                    Chat("anna") with { IsPinned = true, TrailingText = "10:42" },
                    Chat("team", group: true, Message("m1", Peter, "Done", read: true)) with { IsMuted = true, Detail = "Peter: Done", IsEnabled = false },
                ]),
            ],
        };

        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, Context(test)).Native);

        var anna = Item(native, 0);
        Check.Equal("anna", anna.ConversationIdentifier);
        Check.Equal("Anna", anna.Text);
        Check.Equal("Hi, are you on your way?", anna.DetailText);
        Check.Equal("10:42", anna.TrailingText);
        Check.True(anna.LeadingConfiguration!.Unread);
        Check.Equal(CPMessageLeadingItem.Pin, anna.LeadingConfiguration.LeadingItem);
        Check.Equal(CPMessageTrailingItem.None, anna.TrailingConfiguration!.TrailingItem);
        Check.Equal("anna", (anna.UserInfo as NSString)?.ToString());
        var team = Item(native, 1);
        Check.True(!team.LeadingConfiguration!.Unread);
        Check.Equal(CPMessageTrailingItem.Mute, team.TrailingConfiguration!.TrailingItem);
        Check.Equal("Peter: Done", team.DetailText);
        Check.True(!team.Enabled);
        return Task.CompletedTask;
    }

    private static Task Conversation_rows_update_in_place()
    {
        var test = TestSession.CarPlay(CarAppCategory.Communication);
        var context = Context(test);
        var read = Chat("anna", false, Message("m1", Anna, "Hi", read: true));
        var list = new CarListTemplate("Messages") { Sections = [new CarSection([read])] };
        var state = CarPlayTemplateRenderers.Render(list, context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var before = Item(native, 0);
        Check.True(!before.LeadingConfiguration!.Unread);

        var updated = list with { Sections = [new CarSection([read with { Messages = read.Messages.Add(Message("m2", Anna, "Hello?")), IsMuted = true }])] };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, context));

        var after = Item(native, 0);
        Check.True(ReferenceEquals(before, after));
        Check.Equal("Hello?", after.DetailText);
        Check.True(after.LeadingConfiguration!.Unread);
        Check.Equal(CPMessageTrailingItem.Mute, after.TrailingConfiguration!.TrailingItem);
        return Task.CompletedTask;
    }

    // The category is registered by the module initializer at app start (IMauiInitializeService), without user consent
    private static async Task Message_category_allows_car_play()
    {
        UNNotificationCategory? category = null;
        var start = Environment.TickCount64;
        while (category is null && Environment.TickCount64 - start < 5000)
        {
            var categories = await UNUserNotificationCenter.Current.GetNotificationCategoriesAsync();
            category = categories.ToArray().FirstOrDefault(c => c.Identifier == CarIosMessageNotifier.CategoryId);
            if (category is null)
            {
                await Task.Delay(100);
            }
        }

        Check.NotNull(category);
        Check.True(category!.Options.HasFlag(UNNotificationCategoryOptions.AllowInCarPlay));
        Check.True(category.IntentIdentifiers.Contains(INIntentIdentifier.SendMessage.GetConstant()!.ToString()));
        Check.Equal(2, category.Actions.Length);
        Check.Is<UNTextInputNotificationAction>(category.Actions[0]);
        Check.Equal(CarIosMessageNotifier.ReplyActionId, category.Actions[0].Identifier);
        Check.Equal(CarIosMessageNotifier.MarkAsReadActionId, category.Actions[1].Identifier);
        Check.True(UNUserNotificationCenter.Current.Delegate is CarKitNotificationDelegate);
    }

    private static Task Notification_request_carries_the_conversation()
    {
        var content = CarMessageNotificationContent.From(Chat("team", group: true), Message("m9", Peter, "I'll be late"));

        var request = CarIosMessageNotifier.Request(content);

        Check.Equal("carkit.message.team.m9", request.Identifier);
        Check.Equal("Team", request.Content.Title);
        Check.Equal("Peter", request.Content.Subtitle);
        Check.Equal("I'll be late", request.Content.Body);
        Check.Equal("team", request.Content.ThreadIdentifier);
        Check.Equal(CarIosMessageNotifier.CategoryId, request.Content.CategoryIdentifier);
        return Task.CompletedTask;
    }

    // Siri after a CPMessageListItem is selected: reading (INSearchForMessagesIntent with the conversation identifier),
    // replying (INSendMessageIntent) and marking (INSetMessageAttributeIntent); the sample handler runs in-process,
    // requests reach the app through the store and are received by the module router → ICarMessageHandler
    private static async Task Intents_handler_searches_and_replies_through_the_store()
    {
        var directory = Path.Combine(Path.GetTempPath(), "carkit-intents-" + Guid.NewGuid().ToString("N"));
        var store = new CarConversationStore(directory);
        try
        {
            await store.SaveConversationsAsync([Chat("anna", false, Message("m1", Anna, "Are you there yet?"), Message("m0", Anna, "Old", read: true)), Chat("team", group: true)]);
            var handler = new IntentHandler(store, appGroup: null);

            INSearchForMessagesIntentResponse? found = null;
            handler.HandleSearchForMessages(
                new INSearchForMessagesIntent(null!, null!, null!, INMessageAttributeOptions.Unread, null!, null!, null!, null!, ["anna"]),
                response => found = response);
            Check.Equal(INSearchForMessagesIntentResponseCode.Success, found!.Code);
            var message = found.Messages!.Single();
            Check.Equal("m1", message.Identifier);
            Check.Equal("anna", message.ConversationIdentifier);
            Check.Equal("Are you there yet?", message.Content);
            Check.Equal("Anna", message.Sender!.DisplayName);

            INSendMessageIntentResponse? sent = null;
            handler.HandleSendMessage(new INSendMessageIntent(null!, INOutgoingMessageType.Text, "In 5 minutes", null!, "anna", null!, null!, null!), response => sent = response);
            Check.Equal(INSendMessageIntentResponseCode.Success, sent!.Code);
            INSendMessageIntentResponse? unknown = null;
            handler.HandleSendMessage(new INSendMessageIntent(null!, INOutgoingMessageType.Text, "x", null!, "nope", null!, null!, null!), response => unknown = response);
            Check.Equal(INSendMessageIntentResponseCode.Failure, unknown!.Code);

            INSetMessageAttributeIntentResponse? marked = null;
            handler.HandleSetMessageAttribute(new INSetMessageAttributeIntent(["m1"], INMessageAttribute.Read), response => marked = response);
            Check.Equal(INSetMessageAttributeIntentResponseCode.Success, marked!.Code);

            var messages = Services.GetRequiredService<DeviceTestMessageHandler>();
            await Services.GetRequiredService<CarCommunicationRuntime>().Messages.DrainAsync(store);
            Check.True(messages.Calls.Contains("reply:anna:In 5 minutes"));
            Check.True(messages.Calls.Contains("read:anna"));
            Check.Equal(0, (await store.DequeueAllAsync()).Count);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // CallKit probe on the simulator (ledger): the system accepts the report or resets the provider without a
    // completion; in that case CarKit ends the call as Failed and raises CarPlatformException instead of a hanging task
    private static async Task CallKit_reports_and_ends_an_incoming_call()
    {
        var manager = Check.Is<CarCallKitManager>(Services.GetRequiredService<ICarCallManager>());
        var changes = new List<CarCall>();
        manager.CallChanged += (_, call) => changes.Add(call);
        CarCall call;
        try
        {
            call = await manager.ReportIncomingCallAsync(new CarCallHandle(CarCallHandleType.PhoneNumber, "+15550100200"), "Anna").WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (CarPlatformException ex)
        {
            Console.WriteLine($"INFO CallKit_reports_and_ends_an_incoming_call: {ex.Message}; CXCallObserver calls {new CXCallObserver().Calls.Length}");
            Check.Equal(0, manager.Calls.Count);
            Check.Equal("Ringing,Ended", string.Join(",", changes.Select(c => c.State)));
            Check.Equal(CarCallEndReason.Failed, changes[^1].EndReason);
            return;
        }

        Check.Equal(CarCallState.Ringing, call.State);
        var observer = new CXCallObserver();
        await Check.Eventually(() => observer.Calls.Any(c => c.Uuid.AsString().Equals(call.Id.ToString(), StringComparison.OrdinalIgnoreCase)), "CallKit call observer");
        Console.WriteLine("INFO CallKit_reports_and_ends_an_incoming_call: simulator accepted the incoming call");

        await manager.EndCallAsync(call.Id);
        await Check.Eventually(() => manager.Calls.Count == 0, "call ended through CXEndCallAction");
        Check.True(Services.GetRequiredService<DeviceTestCallHandler>().Calls.Contains("end:" + call.Id));
        Check.Equal(CarCallState.Ended, changes[^1].State);
    }
}
