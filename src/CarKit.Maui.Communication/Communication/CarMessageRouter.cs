using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.Communication;

// A single entry point for replies and marks coming from outside the car screens: notification actions (both systems)
// and Siri requests from the Intents Extension store → the app's ICarMessageHandler on the main thread. Does not
// throw: a missing handler and its exceptions go to ICarErrorHandler (the native callbacks call it: BroadcastReceiver,
// the UNUserNotificationCenter delegate).
internal sealed class CarMessageRouter(IServiceProvider services, ICarErrorHandler errors, ICarDispatcher dispatcher)
{
    public const string ReplyOperation = "Communication.Reply";
    public const string MarkAsReadOperation = "Communication.MarkAsRead";

    // true = the app handler accepted the reply (no exception); the notification then shows it as sent (review M2)
    public Task<bool> ReplyAsync(string conversationId, string text) =>
        RunAsync(ReplyOperation, (handler, ct) => handler.OnReplyAsync(conversationId, text, ct));

    public Task<bool> MarkAsReadAsync(string conversationId) =>
        RunAsync(MarkAsReadOperation, (handler, ct) => handler.OnMarkAsReadAsync(conversationId, ct));

    // Siri requests in write order; a store read error also goes to ICarErrorHandler
    public async Task DrainAsync(CarConversationStore store)
    {
        IReadOnlyList<CarMessageRequest> requests;
        try
        {
            requests = await store.DequeueAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.HandleError(ex, new CarErrorContext("Communication.Requests", null));
            return;
        }

        foreach (var request in requests)
        {
            await (request.Kind == CarMessageRequestKind.Reply
                ? ReplyAsync(request.ConversationId, request.Text ?? string.Empty)
                : MarkAsReadAsync(request.ConversationId)).ConfigureAwait(false);
        }
    }

    private async Task<bool> RunAsync(string operation, Func<ICarMessageHandler, CancellationToken, Task> call)
    {
        try
        {
            var handler = services.GetService<ICarMessageHandler>()
                ?? throw new InvalidOperationException(
                    "No ICarMessageHandler: call communication.UseMessageHandler<THandler>() inside car.UseCommunication(...) to receive replies and read marks.");
            await dispatcher.DispatchAsync(() => call(handler, CancellationToken.None)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.HandleError(ex, new CarErrorContext(operation, null));
            return false;
        }
    }
}
