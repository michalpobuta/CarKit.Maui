using CarKit.Maui.Communication;

namespace CarKit.Sample.Communication;

// Phone screen: a simulation of incoming messages (a notification with reply and read mark) and VoIP calls through
// CallKit (iOS); the event log shows what came back from the car, notifications and Siri
public sealed class MainPage : ContentPage
{
    private readonly SampleConversations _conversations;
    private readonly ICarMessageNotifier _notifier;
    private readonly ICarCallManager _calls;
    private readonly Label _log = new() { FontSize = 13 };
    private readonly Label _state = new() { FontSize = 16 };
    private int _incoming;

    public MainPage(SampleConversations conversations, ICarMessageNotifier notifier, ICarCallManager calls)
    {
        _conversations = conversations;
        _notifier = notifier;
        _calls = calls;
        Title = "CarKit Messages";
        _conversations.Changed += (_, conversation) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Log($"{conversation.Title.Text}: {conversation.Messages[conversation.Messages.Count - 1].Text}{(conversation.IsUnread ? " (unread)" : string.Empty)}");
            _ = _notifier.PublishConversationsAsync(_conversations.All);
        });
        _calls.CallChanged += (_, call) => MainThread.BeginInvokeOnMainThread(() =>
        {
            _state.Text = call.State == CarCallState.Ended ? "No call" : $"{call.DisplayName}: {call.State}";
            Log($"Call {call.DisplayName}: {call.State}{(call.EndReason is { } reason ? " (" + reason + ")" : string.Empty)}");
        });
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Stage 6: messages and calls", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Conversations and contacts are in CarPlay and Android Auto; here is a simulation of the server and VoIP calls.", FontSize = 14 },
                    Button("Allow notifications", AllowAsync),
                    Button("Incoming message from Anna", () => ReceiveAsync("anna", SampleConversations.Anna)),
                    Button("Incoming message in the team chat", () => ReceiveAsync("team", SampleConversations.Peter)),
                    _state,
                    Button("Incoming call from Olivia", IncomingCallAsync),
                    Button("Call Anna", OutgoingCallAsync),
                    Button("End call", EndCallAsync),
                    _log,
                },
            },
        };
        _state.Text = _calls.IsSupported ? "No call" : "Calls need Core-Telecom on Android (not available yet)";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Conversations for Siri (Intents Extension) from the start, even before a message arrives
        _ = _notifier.PublishConversationsAsync(_conversations.All);
    }

    private async Task AllowAsync() => Log("Notifications allowed: " + await _notifier.RequestPermissionAsync());

    private async Task ReceiveAsync(string conversationId, CarKit.Maui.CarPerson sender)
    {
        var texts = new[] { "Where are you?", "Can you call on the way?", "Please buy bread", "The meeting starts in 10 minutes" };
        var (conversation, message) = _conversations.Receive(conversationId, sender, texts[_incoming++ % texts.Length]);
        await _notifier.NotifyAsync(conversation, message);
    }

    private async Task IncomingCallAsync()
    {
        if (!_calls.IsSupported)
        {
            Log("Calls are not supported on this platform");
            return;
        }

        await _calls.ReportIncomingCallAsync(SampleCommunicationCarHandler.Handle(SampleConversations.Olivia), SampleConversations.Olivia.Name);
    }

    private async Task OutgoingCallAsync()
    {
        if (!_calls.IsSupported)
        {
            Log("Calls are not supported on this platform");
            return;
        }

        await _calls.StartOutgoingCallAsync(SampleCommunicationCarHandler.Handle(SampleConversations.Anna), SampleConversations.Anna.Name);
    }

    private async Task EndCallAsync()
    {
        if (_calls.Calls is [var call, ..])
        {
            await _calls.EndCallAsync(call.Id);
        }
    }

    private void Log(string line) => _log.Text = (DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine + _log.Text).Trim();

    private Button Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"{text}: {ex.GetType().Name}: {ex.Message}");
            }
        };
        return button;
    }
}
