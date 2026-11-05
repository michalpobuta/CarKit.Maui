using CarKit.Maui.Communication;

namespace CarKit.Maui.Tests.Communication;

// Message and conversation handler for tests (records the calls and the thread)
internal sealed class TestMessageHandler : ICarMessageHandler
{
    public List<string> Calls { get; } = [];

    public Exception? Throw { get; set; }

    public Task OnReplyAsync(string conversationId, string text, CancellationToken cancellationToken)
    {
        Calls.Add($"reply:{conversationId}:{text}");
        return Throw is { } ex ? Task.FromException(ex) : Task.CompletedTask;
    }

    public Task OnMarkAsReadAsync(string conversationId, CancellationToken cancellationToken)
    {
        Calls.Add($"read:{conversationId}");
        return Task.CompletedTask;
    }
}

internal sealed class TestCallHandler : ICarCallHandler
{
    public Task<bool> OnAnswerAsync(CarCall call) => Task.FromResult(false);
}

internal static class Conversations
{
    public static readonly CarPerson Me = new("me", "Me");
    public static readonly CarPerson Anna = new("anna", "Anna");
    public static readonly CarPerson Peter = new("peter", "Peter");

    public static CarConversationMessage Message(string id, CarPerson? sender, string text) => new(id, sender, text, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

    public static CarConversationItem Chat(string id = "anna", bool group = false, int count = 1) =>
        new(id, group ? "Team" : "Anna", Me, Enumerable.Range(1, count).Select(i => Message("m" + i, Anna, "Message " + i)))
        {
            IsGroupConversation = group,
        };
}
