namespace CarKit.Maui.Communication;

// Message notification content shared by the platforms: the conversation's messages with the new one last (once, by
// Id), max. 25 (NotificationCompat.MessagingStyle.MAXIMUM_RETAINED_MESSAGES), iOS title and subtitle (group:
// conversation title + sender)
internal sealed record CarMessageNotificationContent(
    CarConversationItem Conversation,
    CarConversationMessage Message,
    IReadOnlyList<CarConversationMessage> Messages,
    string Title,
    string? Subtitle,
    string Sender)
{
    public const int MaxMessages = 25;

    public const string ReplyAction = "reply";
    public const string MarkAsReadAction = "mark-as-read";

    public static CarMessageNotificationContent From(CarConversationItem conversation, CarConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(message);
        var messages = conversation.Messages.Any(m => m.Id == message.Id)
            ? [.. conversation.Messages]
            : conversation.Messages.Append(message).ToList();
        var sender = (message.Sender ?? conversation.Self).Name;
        return new CarMessageNotificationContent(
            conversation,
            message,
            messages.Count > MaxMessages ? messages.GetRange(messages.Count - MaxMessages, MaxMessages) : messages,
            conversation.IsGroupConversation ? conversation.Title.Text : sender,
            conversation.IsGroupConversation ? sender : null,
            sender);
    }

    // Notification after a reply from an action: the reply as a Self message (Android Auto and the system leave no
    // spinner)
    public CarMessageNotificationContent WithReply(string text, DateTimeOffset now)
    {
        var reply = new CarConversationMessage("carkit-reply-" + now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), null, text, now) { IsRead = true };
        return From(Conversation with { Messages = [.. Messages] }, reply) with { Title = Title, Subtitle = Subtitle, Sender = Sender };
    }

    // PendingIntent requestCode: stable across processes (string.GetHashCode is randomized per process) and different
    // for conversations and actions, so the PendingIntents of two conversations do not overwrite each other (FNV-1a, 31
    // bits)
    public static int RequestCode(string conversationId, string action)
    {
        var hash = 2166136261u;
        foreach (var c in conversationId + "|" + action)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}
