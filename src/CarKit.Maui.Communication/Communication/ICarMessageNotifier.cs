namespace CarKit.Maui.Communication;

/// <summary>
/// Shows incoming messages so the car can read them and the driver can reply: an Android <c>MessagingStyle</c> notification
/// with Reply and Mark as read actions (required by Android Auto), and an iOS notification in a category allowed in CarPlay.
/// Replies and read marks arrive at <see cref="ICarMessageHandler"/>.
/// </summary>
public interface ICarMessageNotifier
{
    /// <summary>
    /// Asks the user to allow notifications (Android 13+: <c>POST_NOTIFICATIONS</c>; iOS: alerts, sounds, badges and CarPlay).
    /// Call it from the phone UI.
    /// </summary>
    /// <returns><see langword="true"/> when the app may show notifications.</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Shows <paramref name="message"/> of <paramref name="conversation"/>. A message that is not in
    /// <see cref="CarConversationItem.Messages"/> is added after them. Without permission the call logs a warning.
    /// </summary>
    /// <param name="conversation">The conversation; its <see cref="CarElement.Id"/> identifies the notification.</param>
    /// <param name="message">The new message.</param>
    /// <returns>A task that completes when the notification is posted.</returns>
    Task NotifyAsync(CarConversationItem conversation, CarConversationMessage message);

    /// <summary>Removes the notification of a conversation.</summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <returns>A task that completes when the notification is removed.</returns>
    Task CancelAsync(string conversationId);

    /// <summary>
    /// Saves the conversations Siri may read and reply to through the app's Intents Extension (iOS, App Group from
    /// <c>&lt;CarKitIosAppGroup&gt;</c>). Does nothing on Android.
    /// </summary>
    /// <param name="conversations">The conversations.</param>
    /// <returns>A task that completes when the conversations are saved.</returns>
    Task PublishConversationsAsync(IEnumerable<CarConversationItem> conversations);
}
