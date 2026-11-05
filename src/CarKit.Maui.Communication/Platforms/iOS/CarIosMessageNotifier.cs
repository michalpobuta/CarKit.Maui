using Foundation;
using Intents;
using ObjCRuntime;
using UserNotifications;

namespace CarKit.Maui.Communication;

// ICarMessageNotifier on UserNotifications (spec: "Notifications: iOS"): category carkit.message with AllowInCarPlay
// and an INSendMessageIntent intent (CarPlay reads the message aloud and offers a reply through Siri), Reply (text) and
// Mark as read actions on the phone; thread = conversation Id. The CarKit delegate wraps the delegate set earlier by
// the app.
internal sealed class CarIosMessageNotifier(CarCommunicationRuntime runtime, CarSiriBridge siri) : ICarMessageNotifier, IDisposable
{
    public const string CategoryId = "carkit.message";
    public const string ReplyActionId = "carkit.reply";
    public const string MarkAsReadActionId = "carkit.markAsRead";
    public const string ConversationIdKey = "carkit.conversationId";

    private CarKitNotificationDelegate? _delegate;

    public void Start()
    {
        var center = UNUserNotificationCenter.Current;
        _ = EnsureCategoryAsync();
        if (center.Delegate is not CarKitNotificationDelegate)
        {
            _delegate = new CarKitNotificationDelegate(this, center.Delegate);
            center.Delegate = _delegate;
        }
    }

    // The delegate lives with the app (DI singleton); released when the container is closed
    public void Dispose()
    {
        if (_delegate is { } current && ReferenceEquals(UNUserNotificationCenter.Current.Delegate, current))
        {
            UNUserNotificationCenter.Current.Delegate = current.Previous!;
        }

        _delegate?.Dispose();
        _delegate = null;
    }

    public static UNNotificationCategory Category(CarCommunicationOptions options) =>
        UNNotificationCategory.FromIdentifier(
            CategoryId,
            [
                UNTextInputNotificationAction.FromIdentifier(ReplyActionId, options.ReplyActionTitle, UNNotificationActionOptions.None, options.ReplyActionTitle, string.Empty),
                UNNotificationAction.FromIdentifier(MarkAsReadActionId, options.MarkAsReadActionTitle, UNNotificationActionOptions.None),
            ],
            [INIntentIdentifier.SendMessage.GetConstant()!],
            UNNotificationCategoryOptions.AllowInCarPlay);

    // SetNotificationCategories replaces the whole set, so an app that sets its own categories removes carkit.message
    // (review I4): the category is merged at startup and before every notification
    internal async Task EnsureCategoryAsync()
    {
        var center = UNUserNotificationCenter.Current;
        var categories = (await center.GetNotificationCategoriesAsync())?.ToArray() ?? [];
        if (categories.Any(c => c.Identifier == CategoryId))
        {
            return;
        }

        UNNotificationCategory[] all = [.. categories, Category(runtime.Options)];
        center.SetNotificationCategories(new NSSet<UNNotificationCategory>(all));
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge | UNAuthorizationOptions.CarPlay);
        if (error is not null)
        {
            CarCommunicationLog.Warning(runtime.Logger, "notification authorization failed: " + error.LocalizedDescription, null);
        }

        // Siri passes message intents to the Intents Extension only after the user consents (review I6). We ask only
        // when the App Group store works: the App Group and Siri entitlements are added by the same device build, and
        // without the Siri entitlement the request ends with an Objective-C exception
        if (siri.Store is not null && INPreferences.SiriAuthorizationStatus == INSiriAuthorizationStatus.NotDetermined)
        {
            var siriStatus = await INPreferences.RequestSiriAuthorizationAsync();
            CarCommunicationLog.Info(runtime.Logger, "Siri authorization: " + siriStatus);
        }

        return granted;
    }

    public async Task NotifyAsync(CarConversationItem conversation, CarConversationMessage message)
    {
        var content = CarMessageNotificationContent.From(conversation, message);
        await EnsureCategoryAsync();
        // A conversation for Siri (read and reply in CarPlay) also when notifications are disabled
        await siri.SaveConversationAsync(conversation with { Messages = [.. content.Messages] });
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        if (settings.AuthorizationStatus is not (UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral))
        {
            CarCommunicationLog.NotificationSkipped(runtime.Logger, conversation.Id, $"notifications are not authorized ({settings.AuthorizationStatus})");
            return;
        }

        await UNUserNotificationCenter.Current.AddNotificationRequestAsync(Request(content));
    }

    public static UNNotificationRequest Request(CarMessageNotificationContent content)
    {
        var notification = new UNMutableNotificationContent
        {
            Title = content.Title,
            Body = content.Message.Text,
            CategoryIdentifier = CategoryId,
            ThreadIdentifier = content.Conversation.Id,
            Sound = UNNotificationSound.Default,
            UserInfo = NSDictionary.FromObjectAndKey(new NSString(content.Conversation.Id), new NSString(ConversationIdKey)),
        };
        if (content.Subtitle is { } subtitle)
        {
            notification.Subtitle = subtitle;
        }

        return UNNotificationRequest.FromIdentifier(CategoryId + "." + content.Conversation.Id + "." + content.Message.Id, notification, null);
    }

    public async Task CancelAsync(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var delivered = await UNUserNotificationCenter.Current.GetDeliveredNotificationsAsync();
        var ids = delivered
            .Where(n => n.Request.Content.CategoryIdentifier == CategoryId && n.Request.Content.ThreadIdentifier == conversationId)
            .Select(n => n.Request.Identifier)
            .ToArray();
        if (ids.Length > 0)
        {
            UNUserNotificationCenter.Current.RemoveDeliveredNotifications(ids);
        }
    }

    public Task PublishConversationsAsync(IEnumerable<CarConversationItem> conversations)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        return siri.SaveConversationsAsync(conversations);
    }

    // A reply from a CarKit notification action; other categories are handled by the app's previous delegate
    internal async Task HandleResponseAsync(UNNotificationResponse response)
    {
        var content = response.Notification.Request.Content;
        var conversationId = (content.UserInfo?[ConversationIdKey] as NSString)?.ToString() ?? content.ThreadIdentifier;
        if (string.IsNullOrEmpty(conversationId))
        {
            return;
        }

        switch (response.ActionIdentifier)
        {
            case ReplyActionId when response is UNTextInputNotificationResponse { UserText: { Length: > 0 } text }:
                await runtime.Messages.ReplyAsync(conversationId, text);
                break;
            case MarkAsReadActionId:
                await runtime.Messages.MarkAsReadAsync(conversationId);
                await CancelAsync(conversationId);
                break;
        }
    }

    internal static bool IsCarKit(UNNotification notification) => notification.Request.Content.CategoryIdentifier == CategoryId;

    internal ICarErrorHandler Errors => runtime.Errors;
}

// UNUserNotificationCenter delegate: replies to and presentation of CarKit notifications, the rest goes to the previous
// delegate (if it implements the method — calling an unsupported selector would crash the process)
internal sealed class CarKitNotificationDelegate(CarIosMessageNotifier notifier, IUNUserNotificationCenterDelegate? previous) : UNUserNotificationCenterDelegate
{
    public IUNUserNotificationCenterDelegate? Previous => previous;

    private static readonly Selector _didReceive = new("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:");
    private static readonly Selector _willPresent = new("userNotificationCenter:willPresentNotification:withCompletionHandler:");

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        if (CarIosMessageNotifier.IsCarKit(response.Notification))
        {
            _ = Complete(notifier.HandleResponseAsync(response), completionHandler, notifier.Errors);
        }
        else if (previous is NSObject other && other.RespondsToSelector(_didReceive))
        {
            previous.DidReceiveNotificationResponse(center, response, completionHandler);
        }
        else
        {
            completionHandler();
        }
    }

    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
    {
        if (CarIosMessageNotifier.IsCarKit(notification))
        {
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);
        }
        else if (previous is NSObject other && other.RespondsToSelector(_willPresent))
        {
            previous.WillPresentNotification(center, notification, completionHandler);
        }
        else
        {
            completionHandler(UNNotificationPresentationOptions.None);
        }
    }

    // An exception (e.g. reading delivered notifications) goes to ICarErrorHandler instead of an unobserved task
    // (review M10)
    private static async Task Complete(Task work, Action completionHandler, ICarErrorHandler errors)
    {
        try
        {
            await work;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.HandleError(ex, new CarErrorContext("Communication.NotificationResponse", null));
        }
        finally
        {
            completionHandler();
        }
    }
}
