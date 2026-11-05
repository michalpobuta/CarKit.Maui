using CarKit.Maui.Communication;

namespace CarKit.Maui.DeviceTests;

// Stage 6: handlers of the test app: replies and read marks from notification actions and Siri, CallKit actions
internal sealed class DeviceTestMessageHandler : ICarMessageHandler
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task OnReplyAsync(string conversationId, string text, CancellationToken cancellationToken) => Record($"reply:{conversationId}:{text}");

    public Task OnMarkAsReadAsync(string conversationId, CancellationToken cancellationToken) => Record($"read:{conversationId}");

    private Task Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call + (MainThread.IsMainThread ? string.Empty : ":off-main"));
        }

        return Task.CompletedTask;
    }
}

internal sealed class DeviceTestCallHandler : ICarCallHandler
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task OnEndAsync(CarCall call)
    {
        lock (_calls)
        {
            _calls.Add("end:" + call.Id);
        }

        return Task.CompletedTask;
    }
}

internal static class DeviceConversations
{
    public static readonly CarPerson Me = new("me", "Me");
    public static readonly CarPerson Anna = new("anna", "Anna") { PhoneOrEmail = "+15550100200" };
    public static readonly CarPerson Peter = new("peter", "Peter");

    public static CarConversationItem Chat(string id, bool group = false, params CarConversationMessage[] messages) =>
        new(id, group ? "Team" : "Anna", Me, messages.Length > 0 ? messages : [Message("m1", Anna, "Hi, are you on your way?")])
        {
            IsGroupConversation = group,
        };

    public static CarConversationMessage Message(string id, CarPerson? sender, string text, bool read = false) =>
        new(id, sender, text, DateTimeOffset.FromUnixTimeSeconds(1_761_800_000)) { IsRead = read };
}
