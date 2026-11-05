using CarKit.Maui;
using CoreFoundation;
using Foundation;
using Intents;

namespace CarKit.Sample.Communication.Intents;

// Principal class of the Intents Extension (NSExtensionPrincipalClass): after a CPMessageListItem is selected, Siri in
// CarPlay reads the conversation (INSearchForMessagesIntent with the conversation identifier), replies
// (INSendMessageIntent) and marks messages as read (INSetMessageAttributeIntent). The app stores the conversations
// (ICarMessageNotifier), while the extension queues replies in the same store and wakes the app with a Darwin
// notification; the app's ICarMessageHandler sends them.
[Register("IntentHandler")]
public sealed class IntentHandler : INExtension, IINSendMessageIntentHandling, IINSearchForMessagesIntentHandling, IINSetMessageAttributeIntentHandling
{
    public const string AppGroup = "group.com.carkit.sample.communication";

    private readonly CarConversationStore? _store;
    private readonly string? _appGroup;

    public IntentHandler()
    {
        // The container exists only with the App Group entitlement (a signed device build)
        if (NSFileManager.DefaultManager.GetContainerUrl(AppGroup)?.Path is { Length: > 0 } container)
        {
            _store = CarConversationStore.ForAppGroupContainer(container);
            _appGroup = AppGroup;
        }
    }

    // Device tests: a store in a temporary directory, without the Darwin notification
    internal IntentHandler(CarConversationStore store, string? appGroup)
    {
        _store = store;
        _appGroup = appGroup;
    }

    public override NSObject GetHandler(INIntent intent) => this;

    public void ResolveRecipients(INSendMessageIntent intent, Action<INSendMessageRecipientResolutionResult[]> completion)
    {
        if (!string.IsNullOrEmpty(intent.ConversationIdentifier))
        {
            completion([]);
            return;
        }

        if (intent.Recipients is not { Length: > 0 } recipients)
        {
            completion([INSendMessageRecipientResolutionResult.NeedsValue]);
            return;
        }

        var conversations = Load();
        completion([.. recipients.Select(recipient =>
        {
            var matches = SampleIntentLogic.Contacts(conversations, recipient.PersonHandle?.Value ?? recipient.DisplayName);
            return matches.Count switch
            {
                1 => INSendMessageRecipientResolutionResult.GetSuccess(Person(matches[0], isMe: false)),
                0 => INSendMessageRecipientResolutionResult.GetUnsupported(INSendMessageRecipientUnsupportedReason.NoAccount),
                _ => INSendMessageRecipientResolutionResult.GetDisambiguation([.. matches.Select(p => Person(p, isMe: false))]),
            };
        })]);
    }

    public void ResolveContent(INSendMessageIntent intent, Action<INStringResolutionResult> completion) =>
        completion(string.IsNullOrWhiteSpace(intent.Content) ? INStringResolutionResult.NeedsValue : INStringResolutionResult.GetSuccess(intent.Content));

    public void HandleSendMessage(INSendMessageIntent intent, Action<INSendMessageIntentResponse> completion)
    {
        var recipients = intent.Recipients?.Select(r => r.CustomIdentifier ?? r.PersonHandle?.Value ?? r.DisplayName).ToList() ?? [];
        if (_store is null || string.IsNullOrWhiteSpace(intent.Content)
            || SampleIntentLogic.FindConversation(Load(), intent.ConversationIdentifier, recipients) is not { } conversation)
        {
            completion(new INSendMessageIntentResponse(_store is null ? INSendMessageIntentResponseCode.FailureMessageServiceNotAvailable : INSendMessageIntentResponseCode.Failure, null));
            return;
        }

        Enqueue(new CarMessageRequest(CarMessageRequestKind.Reply, conversation.Id) { Text = intent.Content });
        completion(new INSendMessageIntentResponse(INSendMessageIntentResponseCode.Success, null));
    }

    public void HandleSearchForMessages(INSearchForMessagesIntent intent, Action<INSearchForMessagesIntentResponse> completion)
    {
        if (_store is null)
        {
            completion(new INSearchForMessagesIntentResponse(INSearchForMessagesIntentResponseCode.FailureMessageServiceNotAvailable, null));
            return;
        }

        var found = SampleIntentLogic.Search(
            Load(),
            intent.ConversationIdentifiers ?? [],
            intent.Identifiers ?? [],
            intent.Senders?.Select(s => s.CustomIdentifier ?? s.PersonHandle?.Value ?? s.DisplayName).ToList() ?? [],
            intent.Attributes.HasFlag(INMessageAttributeOptions.Unread));
        completion(new INSearchForMessagesIntentResponse(INSearchForMessagesIntentResponseCode.Success, null)
        {
            Messages = [.. found.Select(x => Message(x.Conversation, x.Message))],
        });
    }

    public void HandleSetMessageAttribute(INSetMessageAttributeIntent intent, Action<INSetMessageAttributeIntentResponse> completion)
    {
        var conversations = intent.Attribute == INMessageAttribute.Read && _store is not null
            ? SampleIntentLogic.ConversationsOf(Load(), intent.Identifiers ?? [])
            : [];
        foreach (var id in conversations)
        {
            Enqueue(new CarMessageRequest(CarMessageRequestKind.MarkAsRead, id));
        }

        completion(new INSetMessageAttributeIntentResponse(
            conversations.Count > 0 ? INSetMessageAttributeIntentResponseCode.Success : INSetMessageAttributeIntentResponseCode.FailureMessageNotFound, null));
    }

    // The extension is short-lived and answers through completion: a synchronous read of a small file is the simplest
    // option here
    private IReadOnlyList<CarConversationItem> Load() => _store?.LoadConversationsAsync().GetAwaiter().GetResult() ?? [];

    private void Enqueue(CarMessageRequest request)
    {
        _store!.EnqueueAsync(request).GetAwaiter().GetResult();
        if (_appGroup is not null)
        {
            CFNotificationCenter.Darwin.PostNotification(CarConversationStore.GetRequestsNotificationName(_appGroup), null!, null!, true, true);
        }
    }

    private static INMessage Message(CarConversationItem conversation, CarConversationMessage message)
    {
        var sender = Person(message.Sender ?? conversation.Self, isMe: message.Sender is null);
        INPerson[] recipients = [Person(conversation.Self, isMe: true)];
        var group = conversation.IsGroupConversation ? new INSpeakableString(conversation.Title.Text) : null!;
        var date = (NSDate)message.Timestamp.UtcDateTime;
        if (OperatingSystem.IsIOSVersionAtLeast(18))
        {
            return new INMessage(message.Id, conversation.Id, message.Text, date, sender, recipients, group, null!, INMessageType.Text, null!, null!);
        }

#pragma warning disable CA1422 // The constructor without reactions is obsolete since iOS 18; this is the path for iOS 15–17
        return new INMessage(message.Id, conversation.Id, message.Text, date, sender, recipients, group, INMessageType.Text);
#pragma warning restore CA1422
    }

    private static INPerson Person(CarPerson person, bool isMe) =>
        new(
            new INPersonHandle(person.PhoneOrEmail ?? person.Key, person.PhoneOrEmail is null ? INPersonHandleType.Unknown
                : person.PhoneOrEmail.Contains('@', StringComparison.Ordinal) ? INPersonHandleType.EmailAddress : INPersonHandleType.PhoneNumber),
            null!,
            person.Name,
            null!,
            null!,
            person.Key,
            isMe);
}
