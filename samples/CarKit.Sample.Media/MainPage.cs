using CarKit.Maui.Media;

namespace CarKit.Sample.Media;

// Phone screen: the same player as in the car (commands, state) and a position log every second, proof that the sound
// is playing
public sealed class MainPage : ContentPage, IObserver<CarPlaybackState>
{
    private readonly SampleMediaPlayer _player;
    private readonly Label _status = new() { FontSize = 18, Text = "Nothing playing" };
    private readonly Label _details = new() { FontSize = 14 };
    private IDisposable? _subscription;
    private IDispatcherTimer? _timer;

    public MainPage(SampleMediaPlayer player)
    {
        _player = player;
        Title = "CarKit Media";
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Stage 5: media", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "The library and Now Playing are in CarPlay and Android Auto; here are the same commands.", FontSize = 14 },
                    _status,
                    _details,
                    Button("Play the \"Morning\" album", () => _player.PlayAsync("morning-1")),
                    Button("Pause / resume", () => _player.Current.Status == CarPlaybackStatus.Playing ? _player.PauseAsync() : _player.ResumeAsync()),
                    Button("Next", _player.SkipNextAsync),
                    Button("Shuffle", () => _player.SetShuffleAsync(!_player.Current.IsShuffleEnabled)),
                    Button("Repeat", () => _player.SetRepeatAsync(_player.Current.RepeatMode switch
                    {
                        CarRepeatMode.Off => CarRepeatMode.All,
                        CarRepeatMode.All => CarRepeatMode.One,
                        _ => CarRepeatMode.Off,
                    })),
                    Button("Rate 1× / 1.5×", () => _player.SetRateAsync(_player.Current.Rate > 1 ? 1f : 1.5f)),
                    // Stage 9: a broadcast with the sports mode on the CarPlay Now Playing screen (iOS 18.4)
                    Button("Match broadcast (sports mode)", () => _player.PlayAsync("live-match")),
                },
            },
        };
    }

    public void OnNext(CarPlaybackState value) => MainThread.BeginInvokeOnMainThread(() => Show(value));

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Stage 9: a run without touch (screenshots in the ledger): SIMCTL_CHILD_CARKIT_SAMPLE_AUTORUN=live-match
        if (_subscription is null && Environment.GetEnvironmentVariable("CARKIT_SAMPLE_AUTORUN") == "live-match")
        {
            _ = _player.PlayAsync("live-match");
        }

        _subscription ??= _player.State.Subscribe(this);
        if (_timer is null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) =>
            {
                _player.LogPosition();
                Show(_player.Current);
            };
            _timer.Start();
        }
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) => await action();
        return button;
    }

    private void Show(CarPlaybackState state)
    {
        _status.Text = state.Item is { } item ? $"{state.Status}: {item.Title} — {item.Subtitle}" : "Nothing playing";
        _details.Text = $"Queue {state.QueueIndex + 1}/{state.Queue.Count} · shuffle {(state.IsShuffleEnabled ? "on" : "off")} · repeat {state.RepeatMode} · rate {state.Rate:0.##}×"
            + (state.Item?.Sports is { } sports ? $"{Environment.NewLine}Sports mode: {sports.LeftTeam.Name} {sports.LeftTeam.Score}:{sports.RightTeam.Score} {sports.RightTeam.Name}" : string.Empty);
    }
}
