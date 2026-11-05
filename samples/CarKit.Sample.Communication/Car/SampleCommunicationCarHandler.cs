using System.Globalization;
using CarKit.Maui;
using CarKit.Maui.Communication;

namespace CarKit.Sample.Communication;

// Car screens of the Communication sample (spec: "Communication module decisions (Stage 6)", Sample): a list of
// conversations (CPMessageListItem: Siri reads the selected one; ConversationItem: reading and a voice reply in Android
// Auto) and contacts → CarPlayContactTemplate with Call (CallKit) and Message (Siri) buttons, on Android Auto an
// explicit pane fallback
public sealed class SampleCommunicationCarHandler(SampleConversations conversations, ICarCallManager calls) : ICarAppHandler
{
    public const string RootId = "messages";

    private ICarSession? _session;

    public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        _session = session;
        conversations.Changed += OnChanged;
        await session.SetRootAsync(Root());
    }

    public Task OnDisconnectedAsync(ICarSession session)
    {
        conversations.Changed -= OnChanged;
        _session = null;
        return Task.CompletedTask;
    }

    public CarListTemplate Root() => new("Messages")
    {
        Id = RootId,
        Sections =
        [
            new CarSection(conversations.All.Select(Row)) { Id = "conversations", Header = "Conversations" },
            new CarSection(SampleConversations.Contacts.Select(ContactRow)) { Id = "contacts", Header = "Contacts" },
        ],
    };

    // Voice reply and read mark from Android Auto (ConversationCallback); on CarPlay Siri does this through the Intents
    // Extension
    private CarConversationItem Row(CarConversationItem conversation) => conversation with
    {
        TrailingText = conversation.Messages.IsEmpty ? null : conversation.Messages[conversation.Messages.Count - 1].Timestamp.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
        OnReply = context =>
        {
            conversations.Reply(conversation.Id, context.Text);
            return Task.CompletedTask;
        },
        OnMarkAsRead = _ =>
        {
            conversations.MarkAsRead(conversation.Id);
            return Task.CompletedTask;
        },
    };

    private CarListItem ContactRow(CarPerson person) => new(person.Name)
    {
        Id = "contact-" + person.Key,
        Detail = person.PhoneOrEmail is { } detail ? new CarText(detail) : null,
        IsBrowsable = true,
        OnSelected = context => context.Session.PushAsync(Contact(person)),
    };

    public CarTemplate Contact(CarPerson person) => new CarPlayContactTemplate(person.Name)
    {
        Id = "contact-card-" + person.Key,
        Subtitle = person.PhoneOrEmail is { } subtitle ? new CarText(subtitle) : null,
        Buttons =
        [
            new CarPlayContactButton(CarPlayContactButtonKind.Call) { Id = "call-" + person.Key, OnSelected = _ => CallAsync(person) },
            new CarPlayContactButton(CarPlayContactButtonKind.Message) { Id = "message-" + person.Key, PhoneOrEmail = person.PhoneOrEmail },
        ],
    }.WithFallback(new CarInformationTemplate(person.Name)
    {
        // Android Auto: no contact template and no calls (no Core-Telecom binding), only the contact data
        Items = [new CarInformationItem("Phone or e-mail", person.PhoneOrEmail ?? "—")],
    });

    // CallKit shows the call in the CarPlay Phone app; SampleCallHandler connects it after 2 s
    private Task CallAsync(CarPerson person) =>
        calls.IsSupported
            ? calls.StartOutgoingCallAsync(Handle(person), person.Name)
            : Task.CompletedTask;

    public static CarCallHandle Handle(CarPerson person) =>
        person.PhoneOrEmail is { } handle
            ? new CarCallHandle(handle.Contains('@', StringComparison.Ordinal) ? CarCallHandleType.EmailAddress : CarCallHandleType.PhoneNumber, handle)
            : new CarCallHandle(CarCallHandleType.Generic, person.Key);

    // A new message or reply: the list is updated in place (CarPlay through CPMessageListItem setters, Android Auto by
    // a refresh)
    private void OnChanged(object? sender, CarConversationItem conversation)
    {
        if (_session is { IsConnected: true } session && session.Stack.Any(t => t.Id == RootId))
        {
            _ = session.UpdateAsync(Root());
        }
    }
}
