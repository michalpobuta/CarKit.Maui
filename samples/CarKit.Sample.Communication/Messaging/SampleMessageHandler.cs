using CarKit.Maui.Communication;
using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Communication;

// Replies and read marks from outside the car screens (notification actions, Siri from the Intents Extension) → the sample
// conversations; a messenger would send the reply to the server here
public sealed class SampleMessageHandler(SampleConversations conversations, ILogger<SampleMessageHandler> logger) : ICarMessageHandler
{
    public Task OnReplyAsync(string conversationId, string text, CancellationToken cancellationToken)
    {
        SampleLog.Info(logger, $"reply to {conversationId}: {text}");
        conversations.Reply(conversationId, text);
        return Task.CompletedTask;
    }

    public Task OnMarkAsReadAsync(string conversationId, CancellationToken cancellationToken)
    {
        SampleLog.Info(logger, conversationId + " marked as read");
        conversations.MarkAsRead(conversationId);
        return Task.CompletedTask;
    }
}
