using CarKit.Maui;

namespace CarKit.Sample.Communication;

// In-memory sample conversations (instead of a messenger server): incoming messages, replies and read marks from the car,
// notifications and Siri change the same state; Changed refreshes the car screen and the phone
public sealed class SampleConversations
{
    public static readonly CarPerson Me = new("me", "Me");
    public static readonly CarPerson Anna = new("anna", "Anna Brown") { PhoneOrEmail = "+15550100200" };
    public static readonly CarPerson Peter = new("peter", "Peter Wilson") { PhoneOrEmail = "peter@example.com" };
    public static readonly CarPerson Olivia = new("olivia", "Olivia Green") { PhoneOrEmail = "+15550300400" };

    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private readonly List<CarConversationItem> _conversations;
    private int _nextMessage = 100;

    public SampleConversations(TimeProvider time)
    {
        _time = time;
        var start = time.GetUtcNow().AddMinutes(-30);
        _conversations =
        [
            new("anna", "Anna Brown", Me, [Message("m1", Anna, "Are you on your way yet?", start), Message("m2", null, "Yes, in 20 minutes", start.AddMinutes(2), read: true)]),
            new("team", "Project team", Me, [Message("m3", Peter, "Meeting moved to 3:00 PM", start.AddMinutes(10), read: true), Message("m4", Olivia, "I'm bringing the projector", start.AddMinutes(12), read: true)])
            {
                IsGroupConversation = true,
                IsMuted = true,
            },
        ];
    }

    public SampleConversations()
        : this(TimeProvider.System)
    {
    }

    public event EventHandler<CarConversationItem>? Changed;

    public static IReadOnlyList<CarPerson> Contacts { get; } = [Anna, Peter, Olivia];

    public IReadOnlyList<CarConversationItem> All
    {
        get
        {
            lock (_sync)
            {
                return [.. _conversations];
            }
        }
    }

    public CarConversationItem? Find(string id)
    {
        lock (_sync)
        {
            return _conversations.FirstOrDefault(c => c.Id == id);
        }
    }

    // A simulated incoming message (a button on the phone) → a new message in the conversation
    public (CarConversationItem Conversation, CarConversationMessage Message) Receive(string conversationId, CarPerson sender, string text)
    {
        CarConversationMessage message;
        CarConversationItem updated;
        lock (_sync)
        {
            message = Message("m" + _nextMessage++, sender, text, _time.GetUtcNow());
            var index = _conversations.FindIndex(c => c.Id == conversationId);
            if (index < 0)
            {
                throw new ArgumentException($"Unknown conversation '{conversationId}'.", nameof(conversationId));
            }

            updated = _conversations[index] with { Messages = _conversations[index].Messages.Add(message) };
            _conversations[index] = updated;
        }

        Changed?.Invoke(this, updated);
        return (updated, message);
    }

    // A reply (car, notification, Siri): a message from the user, and the conversation is marked as read
    public CarConversationItem? Reply(string conversationId, string text) =>
        Update(conversationId, c => c with
        {
            Messages = [.. c.Messages.Select(m => m with { IsRead = true }), Message("m" + Interlocked.Increment(ref _nextMessage), null, text, _time.GetUtcNow(), read: true)],
        });

    public CarConversationItem? MarkAsRead(string conversationId) =>
        Update(conversationId, c => c with { Messages = [.. c.Messages.Select(m => m with { IsRead = true })] });

    private CarConversationItem? Update(string conversationId, Func<CarConversationItem, CarConversationItem> change)
    {
        CarConversationItem updated;
        lock (_sync)
        {
            var index = _conversations.FindIndex(c => c.Id == conversationId);
            if (index < 0)
            {
                return null;
            }

            updated = change(_conversations[index]);
            _conversations[index] = updated;
        }

        Changed?.Invoke(this, updated);
        return updated;
    }

    private static CarConversationMessage Message(string id, CarPerson? sender, string text, DateTimeOffset time, bool read = false) =>
        new(id, sender, text, time) { IsRead = read };
}
