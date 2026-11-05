namespace CarKit.Maui.Communication;

/// <summary>
/// Receives replies and read marks from the car: notification actions (Android Auto, CarPlay, the phone) and Siri requests
/// queued by the Intents Extension. Register it with <see cref="CarCommunicationBuilder.UseMessageHandler{THandler}"/>.
/// Methods run on the main thread.
/// </summary>
/// <remarks>
/// Siri requests reach the app only while it runs: the extension queues them in the App Group store and the app takes them
/// at start, when it returns to the foreground or when a running app gets the extension's Darwin notification. A suspended
/// or terminated app does not run, so a messaging app should send Siri replies from its Intents Extension and use the
/// store to bring the app's state up to date.
/// </remarks>
public interface ICarMessageHandler
{
    /// <summary>Sends a reply the driver dictated.</summary>
    /// <param name="conversationId">The conversation identifier (<see cref="CarElement.Id"/> of the <see cref="CarConversationItem"/>).</param>
    /// <param name="text">The reply text.</param>
    /// <param name="cancellationToken">Not cancelled by CarKit today; reserved for a future time limit.</param>
    /// <returns>A task that completes when the reply is handled; a fault keeps the reply out of the notification.</returns>
    Task OnReplyAsync(string conversationId, string text, CancellationToken cancellationToken);

    /// <summary>Marks the conversation as read after the car read it aloud.</summary>
    /// <param name="conversationId">The conversation identifier.</param>
    /// <param name="cancellationToken">Not cancelled by CarKit today; reserved for a future time limit.</param>
    /// <returns>A task that completes when the conversation is marked.</returns>
    Task OnMarkAsReadAsync(string conversationId, CancellationToken cancellationToken);
}
