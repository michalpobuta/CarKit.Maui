using CarKit.Maui;

namespace CarKit.Sample.Communication.Intents;

// Extension logic without the Intents types (also tested on net10.0): choosing the conversation for a reply, searching
// for messages to be read by Siri, and finding the conversations of messages to be marked as read
public static class SampleIntentLogic
{
    // Reply: the conversation from the Siri identifier (selection of a CPMessageListItem row), otherwise the only 1:1
    // conversation with a recipient matched by key, handle or name
    public static CarConversationItem? FindConversation(IReadOnlyList<CarConversationItem> conversations, string? conversationId, IReadOnlyList<string> recipients)
    {
        if (!string.IsNullOrEmpty(conversationId))
        {
            return conversations.FirstOrDefault(c => c.Id == conversationId);
        }

        var matches = conversations
            .Where(c => !c.IsGroupConversation && recipients.Count > 0 && recipients.All(r => Participants(c).Any(p => Matches(p, r))))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    // Recipients from the conversations: people from the messages other than the user, by key
    public static IReadOnlyList<CarPerson> Contacts(IReadOnlyList<CarConversationItem> conversations, string spoken) =>
        [.. conversations.SelectMany(Participants).Where(p => Matches(p, spoken)).DistinctBy(p => p.Key)];

    public static IReadOnlyList<(CarConversationItem Conversation, CarConversationMessage Message)> Search(
        IReadOnlyList<CarConversationItem> conversations,
        IReadOnlyCollection<string> conversationIds,
        IReadOnlyCollection<string> messageIds,
        IReadOnlyCollection<string> senders,
        bool unreadOnly) =>
        [.. conversations
            .Where(c => conversationIds.Count == 0 || conversationIds.Contains(c.Id))
            .SelectMany(c => c.Messages.Select(m => (Conversation: c, Message: m)))
            .Where(x => x.Message.Sender is not null)
            .Where(x => messageIds.Count == 0 || messageIds.Contains(x.Message.Id))
            .Where(x => senders.Count == 0 || senders.Any(s => Matches(x.Message.Sender!, s)))
            .Where(x => !unreadOnly || !x.Message.IsRead)
            .OrderBy(x => x.Message.Timestamp)];

    public static IReadOnlyList<string> ConversationsOf(IReadOnlyList<CarConversationItem> conversations, IReadOnlyCollection<string> messageIds) =>
        [.. conversations.Where(c => c.Messages.Any(m => messageIds.Contains(m.Id))).Select(c => c.Id)];

    private static IEnumerable<CarPerson> Participants(CarConversationItem conversation) =>
        conversation.Messages.Select(m => m.Sender).OfType<CarPerson>().Where(p => p.Key != conversation.Self.Key).DistinctBy(p => p.Key);

    private static bool Matches(CarPerson person, string value) =>
        string.Equals(person.Key, value, StringComparison.Ordinal)
        || string.Equals(person.PhoneOrEmail, value, StringComparison.OrdinalIgnoreCase)
        || string.Equals(person.Name, value, StringComparison.OrdinalIgnoreCase);
}
