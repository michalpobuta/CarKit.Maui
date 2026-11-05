namespace CarKit.Maui;

/// <summary>A participant of a conversation (the user or another person).</summary>
public sealed record CarPerson
{
    /// <summary>Creates the person.</summary>
    /// <param name="key">A stable key that identifies the person in the app (Android <c>Person.key</c>, Siri custom identifier).</param>
    /// <param name="name">The display name.</param>
    public CarPerson(string key, string name)
    {
        Key = key;
        Name = name;
    }

    /// <summary>Gets the stable key of the person.</summary>
    public string Key
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Gets the display name.</summary>
    public string Name
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Gets the phone number or e-mail address Siri uses for the person.</summary>
    public string? PhoneOrEmail { get; init; }
}

/// <summary>A message in a <see cref="CarConversationItem"/>.</summary>
public sealed record CarConversationMessage
{
    /// <summary>Creates the message.</summary>
    /// <param name="id">A stable message identifier (Siri <c>INMessage</c> identifier).</param>
    /// <param name="sender">The sender, or <see langword="null"/> for a message from <see cref="CarConversationItem.Self"/>.</param>
    /// <param name="text">The message text.</param>
    /// <param name="timestamp">When the message was sent or received.</param>
    public CarConversationMessage(string id, CarPerson? sender, string text, DateTimeOffset timestamp)
    {
        Id = id;
        Sender = sender;
        Text = text;
        Timestamp = timestamp;
    }

    /// <summary>Gets the message identifier.</summary>
    public string Id
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>Gets the sender, or <see langword="null"/> for a message from the user.</summary>
    public CarPerson? Sender { get; init; }

    /// <summary>Gets the message text.</summary>
    public string Text { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets when the message was sent or received.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets a value indicating whether the user has read the message.</summary>
    public bool IsRead { get; init; }
}

/// <summary>
/// A conversation row of a <see cref="CarSection"/> (CarPlay <c>CPMessageListItem</c>, Android Auto <c>ConversationItem</c>).
/// Allowed only in the <see cref="CarAppCategory.Communication"/> category. The car host opens the conversation itself
/// (CarPlay hands it to Siri, Android Auto reads it aloud), so <see cref="CarListTemplateItem.OnSelected"/> is not supported.
/// </summary>
public sealed record CarConversationItem : CarListTemplateItem
{
    /// <summary>Creates the row.</summary>
    /// <param name="id">
    /// The conversation identifier; it is also the element <see cref="CarElement.Id"/>, the CarPlay and Siri conversation
    /// identifier and the notification thread.
    /// </param>
    /// <param name="title">The conversation title (the other person or the group name).</param>
    /// <param name="self">The user of the app.</param>
    /// <param name="messages">The messages, oldest first; at least one.</param>
    public CarConversationItem(string id, CarText title, CarPerson self, IEnumerable<CarConversationMessage> messages)
    {
        Id = id;
        Title = title;
        Self = self;
        Messages = messages?.ToCarArray() ?? throw new ArgumentNullException(nameof(messages));
    }

    /// <summary>Gets the conversation title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the user of the app.</summary>
    public CarPerson Self { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the messages, oldest first.</summary>
    public CarArray<CarConversationMessage> Messages { get; init; }

    /// <summary>Gets the conversation image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets a value indicating whether the conversation has more than two participants.</summary>
    public bool IsGroupConversation { get; init; }

    /// <summary>Gets the detail text on CarPlay; defaults to the text of the last message.</summary>
    public CarText? Detail { get; init; }

    /// <summary>Gets the trailing text on CarPlay, for example the time of the last message.</summary>
    public string? TrailingText { get; init; }

    /// <summary>Gets a value indicating whether CarPlay shows the pin mark.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Gets a value indicating whether CarPlay shows the mute mark.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Gets a value indicating whether a message is unread.</summary>
    public bool IsUnread => Messages.Any(m => !m.IsRead);

    /// <summary>
    /// Gets the handler called with the text when the driver replies by voice (Android Auto). On CarPlay Siri sends
    /// replies through the app's Intents Extension.
    /// </summary>
    public Func<CarTextInputContext, Task>? OnReply { get; init; }

    /// <summary>Gets the handler called when the driver has listened to the conversation (Android Auto).</summary>
    public Func<CarItemContext, Task>? OnMarkAsRead { get; init; }

    // CarPlay detail content: an explicit Detail or the last message
    internal string? DetailText => Detail?.Text ?? (Messages.IsEmpty ? null : Messages[Messages.Count - 1].Text);
}
