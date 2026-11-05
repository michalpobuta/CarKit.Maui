using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using CarKit.Maui.Renderers;
using Microsoft.Maui.ApplicationModel;
using AndroidUri = Android.Net.Uri;
using NotificationRemoteInput = AndroidX.Core.App.RemoteInput;

namespace CarKit.Maui.Communication;

// ICarMessageNotifier on NotificationCompat (spec: "Notifications: Android"): MessagingStyle with Self, Reply actions
// (RemoteInput, SEMANTIC_ACTION_REPLY) and Mark as read (SEMANTIC_ACTION_MARK_AS_READ), both without UI, to the
// generated receiver. Notification tag = conversation Id; the last content is kept in process memory for updates after
// a reply.
internal sealed class CarAndroidMessageNotifier(CarCommunicationRuntime runtime, Context context) : ICarMessageNotifier
{
    public const string ChannelId = "carkit_messages";
    public const int NotificationId = 6001;
    public const string ReplyKey = "carkit.reply";
    public const string ConversationIdExtra = "carkit.conversationId";
    public const string ReplyAction = "carkit.maui.communication.REPLY";
    public const string MarkAsReadAction = "carkit.maui.communication.MARK_AS_READ";
    public const string ReceiverClassName = "carkit.maui.generated.CarKitGeneratedMessageReceiver";

    private readonly ConcurrentDictionary<string, CarMessageNotificationContent> _posted = new(StringComparer.Ordinal);

    public Context Context { get; } = context;

    public async Task<bool> RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
                return status == PermissionStatus.Granted;
            }
            catch (Exception ex) when (ex is PermissionException or InvalidOperationException or NullReferenceException)
            {
                // Without an Activity (e.g. only the car service) MAUI will not show the permission prompt
                CarCommunicationLog.Warning(runtime.Logger, "notification permission cannot be requested without an activity", ex);
            }
        }

        return NotificationManagerCompat.From(Context)!.AreNotificationsEnabled();
    }

    public Task NotifyAsync(CarConversationItem conversation, CarConversationMessage message)
    {
        var content = CarMessageNotificationContent.From(conversation, message);
        Post(content, alert: true);
        return Task.CompletedTask;
    }

    public Task CancelAsync(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        Cancel(conversationId);
        return Task.CompletedTask;
    }

    // Siri exists only on iOS
    public Task PublishConversationsAsync(IEnumerable<CarConversationItem> conversations)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        return Task.CompletedTask;
    }

    // Action from the receiver: the app handler, then the reply in the notification (no spinner) or its removal
    public async Task HandleActionAsync(string action, string conversationId, Intent intent)
    {
        switch (action)
        {
            case ReplyAction:
                var text = NotificationRemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(ReplyKey)?.ToString();
                var sent = false;
                if (string.IsNullOrWhiteSpace(text))
                {
                    CarCommunicationLog.Warning(runtime.Logger, $"reply action for '{conversationId}' without text", null);
                }
                else
                {
                    sent = await runtime.Messages.ReplyAsync(conversationId, text);
                }

                // Every reply from RemoteInput must refresh the notification (otherwise the spinner stays): sent →
                // appended as a Self message, not sent (empty text, no handler, exception) → without it (review M2, M3)
                if (_posted.TryGetValue(conversationId, out var posted))
                {
                    Post(sent ? posted.WithReply(text!, DateTimeOffset.UtcNow) : posted, alert: false);
                }
                else
                {
                    // The process started for a reply (no content in memory): without content we cannot rebuild the
                    // conversation, so we remove it
                    Cancel(conversationId);
                }

                break;
            case MarkAsReadAction:
                await runtime.Messages.MarkAsReadAsync(conversationId);
                Cancel(conversationId);
                break;
        }
    }

    private void Post(CarMessageNotificationContent content, bool alert)
    {
        var conversation = content.Conversation;
        var manager = NotificationManagerCompat.From(Context)!;
        if (!manager.AreNotificationsEnabled())
        {
            CarCommunicationLog.NotificationSkipped(runtime.Logger, conversation.Id, "notifications are disabled or POST_NOTIFICATIONS is not granted");
            return;
        }

        EnsureChannel();
        var self = ConversationRenderer.Person(conversation.Self);
        var style = new NotificationCompat.MessagingStyle(self).SetGroupConversation(conversation.IsGroupConversation)!;
        if (conversation.IsGroupConversation)
        {
            style.SetConversationTitle(conversation.Title.Text);
        }

        foreach (var message in content.Messages)
        {
            style.AddMessage(new NotificationCompat.MessagingStyle.Message(
                message.Text, message.Timestamp.ToUnixTimeMilliseconds(), message.Sender is { } sender ? ConversationRenderer.Person(sender) : null));
        }

        var icon = SmallIcon();
        var builder = new NotificationCompat.Builder(Context, ChannelId)
            .SetSmallIcon(icon)!
            .SetStyle(style)!
            .SetCategory(NotificationCompat.CategoryMessage)!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetWhen(content.Message.Timestamp.ToUnixTimeMilliseconds())!
            .SetOnlyAlertOnce(!alert)!
            .AddAction(ReplyActionFor(conversation.Id, icon))!
            .AddAction(MarkAsReadActionFor(conversation.Id, icon))!;
        if (Context.PackageManager?.GetLaunchIntentForPackage(Context.PackageName!) is { } launch)
        {
            builder.SetContentIntent(PendingIntent.GetActivity(Context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        }

        _posted[conversation.Id] = content;
        manager.Notify(conversation.Id, NotificationId, builder.Build());
    }

    private void Cancel(string conversationId)
    {
        _posted.TryRemove(conversationId, out _);
        NotificationManagerCompat.From(Context)!.Cancel(conversationId, NotificationId);
    }

    private NotificationCompat.Action ReplyActionFor(string conversationId, int icon)
    {
        var input = new NotificationRemoteInput.Builder(ReplyKey).SetLabel(runtime.Options.ReplyActionTitle)!.Build()!;
        // RemoteInput requires a PendingIntent with FLAG_MUTABLE (Android 12+); an explicit component, because a
        // mutable PendingIntent with an implicit intent is forbidden since Android 14
        var flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
        var intent = PendingIntent.GetBroadcast(Context, CarMessageNotificationContent.RequestCode(conversationId, CarMessageNotificationContent.ReplyAction),
            ActionIntent(ReplyAction, conversationId), flags)!;
        return new NotificationCompat.Action.Builder(icon, runtime.Options.ReplyActionTitle, intent)
            .AddRemoteInput(input)!
            .SetAllowGeneratedReplies(true)!
            .SetSemanticAction(NotificationCompat.Action.SemanticActionReply)!
            .SetShowsUserInterface(false)!
            .Build()!;
    }

    private NotificationCompat.Action MarkAsReadActionFor(string conversationId, int icon)
    {
        var intent = PendingIntent.GetBroadcast(Context, CarMessageNotificationContent.RequestCode(conversationId, CarMessageNotificationContent.MarkAsReadAction),
            ActionIntent(MarkAsReadAction, conversationId), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        return new NotificationCompat.Action.Builder(icon, runtime.Options.MarkAsReadActionTitle, intent)
            .SetSemanticAction(NotificationCompat.Action.SemanticActionMarkAsRead)!
            .SetShowsUserInterface(false)!
            .Build()!;
    }

    // URI data distinguishes the conversation intents (Intent.filterEquals), the extra carries the Id for the receiver
    private Intent ActionIntent(string action, string conversationId) =>
        new Intent(action)
            .SetClassName(Context, ReceiverClassName)
            .SetData(AndroidUri.Parse("carkit://conversation/" + AndroidUri.Encode(conversationId) + "/" + action))!
            .PutExtra(ConversationIdExtra, conversationId)!;

    private int SmallIcon()
    {
        if (runtime.Options.AndroidSmallIcon is { } name
            && Context.Resources?.GetIdentifier(name, "drawable", Context.PackageName) is { } resource and not 0)
        {
            return resource;
        }

        return Context.ApplicationInfo?.Icon ?? Android.Resource.Drawable.SymActionChat;
    }

    private void EnsureChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26)
            && Context.GetSystemService(Context.NotificationService) is NotificationManager manager)
        {
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, runtime.Options.NotificationChannelName, NotificationImportance.High));
        }
    }
}
