namespace CarKit.Maui.Media;

// One per app (spec: "Media module decisions (Stage 5)"): the last player state on the main thread and all the
// commands from CarPlay, Media3 and auto-browse. Commands do not throw: native callbacks invoke them, so a player
// exception goes to ICarErrorHandler (operation Media.<command>). State, event and markers: main thread only.
internal sealed class CarMediaController : IDisposable
{
    // The app player may pause itself at the start of an interruption (iOS stops AVAudioPlayer) a moment before the
    // notification; a pause that fresh still counts as playing at the start of the interruption
    internal static readonly TimeSpan SelfPauseGrace = TimeSpan.FromSeconds(1);

    private readonly ICarMediaPlayer _player;
    private readonly ICarErrorHandler _errors;
    private readonly ICarDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly IDisposable _subscription;
    private long _receivedAt;

    // Emission order: a state from a worker thread waits for the main thread, and a newer one from the main thread
    // may have already arrived
    private long _received;
    private long _applied;
    private long? _stoppedPlayingAt;
    private string? _pendingItem;

    public CarMediaController(ICarMediaPlayer player, CarMediaOptions options, ICarErrorHandler errors, ICarDispatcher dispatcher, TimeProvider time)
    {
        _player = player;
        Options = options;
        _errors = errors;
        _dispatcher = dispatcher;
        _time = time;
        _receivedAt = time.GetTimestamp();
        Interruptions = new CarMediaInterruptions(this);
        _subscription = player.State.Subscribe(new Observer(this));
    }

    public CarMediaOptions Options { get; }

    public CarPlaybackState State { get; private set; } = new();

    public CarMediaInterruptions Interruptions { get; }

    // After every state change, on the main thread
    public event Action<CarPlaybackState>? StateChanged;

    // Position now: from the moment the state was received while Playing (Media3 and MPNowPlayingInfo extrapolate from
    // it)
    public TimeSpan Position => CarPlaybackClock.Position(State, _time.GetElapsedTime(_receivedAt));

    internal bool StoppedPlayingRecently =>
        _stoppedPlayingAt is { } at && _time.GetElapsedTime(at) <= SelfPauseGrace;

    public Task PlayAsync(string itemId)
    {
        _pendingItem = null;
        Interruptions.CancelResume();
        return RunAsync("Play", p => p.PlayAsync(itemId));
    }

    public Task PauseAsync()
    {
        Interruptions.CancelResume();
        return PauseCoreAsync();
    }

    public Task ResumeAsync()
    {
        Interruptions.CancelResume();
        return ResumeCoreAsync();
    }

    public Task TogglePlayPauseAsync() => State.IsActive ? PauseAsync() : ResumeAsync();

    public Task SeekAsync(TimeSpan position) => RunAsync("Seek", p => p.SeekAsync(position < TimeSpan.Zero ? TimeSpan.Zero : position));

    public Task SkipNextAsync() => RunAsync("SkipNext", p => p.SkipNextAsync());

    public Task SkipPreviousAsync() => RunAsync("SkipPrevious", p => p.SkipPreviousAsync());

    public Task SetShuffleAsync(bool on) => RunAsync("SetShuffle", p => p.SetShuffleAsync(on));

    public Task SetRepeatAsync(CarRepeatMode mode) => RunAsync("SetRepeat", p => p.SetRepeatAsync(mode));

    public Task SetRateAsync(float rate) => RunAsync("SetRate", p => p.SetRateAsync(rate));

    public Task InvokeCustomActionAsync(string actionId) => RunAsync("InvokeCustomAction", p => p.InvokeCustomActionAsync(actionId));

    public Task ToggleShuffleAsync() => SetShuffleAsync(!State.IsShuffleEnabled);

    // Off → All → One → Off (order of the Repeat button in CarPlay and Media3)
    public Task CycleRepeatAsync() => SetRepeatAsync(State.RepeatMode switch
    {
        CarRepeatMode.Off => CarRepeatMode.All,
        CarRepeatMode.All => CarRepeatMode.One,
        _ => CarRepeatMode.Off,
    });

    // The next rate from PlaybackRates after the current one (the first after the last; a rate not in the list → the
    // first)
    public Task CycleRateAsync()
    {
        var rates = Options.PlaybackRates;
        var index = IndexOfRate(rates, State.Rate);
        return SetRateAsync(rates[index < 0 ? 0 : (index + 1) % rates.Count]);
    }

    // Default action of a Now Playing button without OnSelected
    public Task PressAsync(CarNowPlayingButton button) => button.Kind switch
    {
        CarNowPlayingButtonKind.Shuffle => ToggleShuffleAsync(),
        CarNowPlayingButtonKind.Repeat => CycleRepeatAsync(),
        CarNowPlayingButtonKind.PlaybackRate => CycleRateAsync(),
        _ => InvokeCustomActionAsync(button.Id),
    };

    // Media3 setMediaItems: plays at once while playback is in progress, otherwise waits for play (the controller may
    // only prepare)
    public Task SetItemAsync(string itemId)
    {
        if (State.IsActive)
        {
            return PlayAsync(itemId);
        }

        _pendingItem = itemId;
        return Task.CompletedTask;
    }

    // Media3 setPlayWhenReady: an item waiting from SetItemAsync → PlayAsync, otherwise Resume/Pause; ongoing playback
    // unchanged
    public Task SetPlayWhenReadyAsync(bool playWhenReady)
    {
        if (!playWhenReady)
        {
            _pendingItem = null;
            return PauseAsync();
        }

        if (_pendingItem is { } pending)
        {
            return PlayAsync(pending);
        }

        return State.IsActive ? Task.CompletedTask : ResumeAsync();
    }

    // Media3 seekTo with the command kind: next/previous, a queue item or a position in the current item
    public Task SeekToAsync(CarMediaSeekKind kind, int itemIndex, TimeSpan position)
    {
        switch (kind)
        {
            case CarMediaSeekKind.Next:
                return SkipNextAsync();
            case CarMediaSeekKind.Previous:
                return SkipPreviousAsync();
            case CarMediaSeekKind.ToItem:
                var playlist = CarPlaybackMapping.Playlist(State, out var current);
                if (itemIndex >= 0 && itemIndex < playlist.Count && itemIndex != current)
                {
                    return PlayAsync(playlist[itemIndex].Id);
                }

                return SeekAsync(position);
            default:
                return SeekAsync(position);
        }
    }

    public void Dispose() => _subscription.Dispose();

    internal Task PauseCoreAsync() => RunAsync("Pause", p => p.PauseAsync());

    internal Task ResumeCoreAsync() => RunAsync("Resume", p => p.ResumeAsync());

    private static int IndexOfRate(IReadOnlyList<float> rates, float rate)
    {
        for (var i = 0; i < rates.Count; i++)
        {
            if (Math.Abs(rates[i] - rate) < 0.001f)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task RunAsync(string command, Func<ICarMediaPlayer, Task> call)
    {
        try
        {
            await call(_player).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errors.HandleError(ex, new CarErrorContext("Media." + command, null));
        }
    }

    private void Apply(CarPlaybackState state, long sequence)
    {
        if (sequence <= _applied)
        {
            return;
        }

        _applied = sequence;
        if (State.IsActive && !state.IsActive)
        {
            _stoppedPlayingAt = _time.GetTimestamp();
        }
        else if (state.IsActive)
        {
            _stoppedPlayingAt = null;
        }

        State = state;
        _receivedAt = _time.GetTimestamp();
        try
        {
            StateChanged?.Invoke(state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errors.HandleError(ex, new CarErrorContext("Media.StateChanged", null));
        }
    }

    private void Receive(CarPlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sequence = Interlocked.Increment(ref _received);
        if (!_dispatcher.IsDispatchRequired)
        {
            Apply(state, sequence);
            return;
        }

        _dispatcher.DispatchAsync(() =>
        {
            Apply(state, sequence);
            return Task.CompletedTask;
        }).Forget(_errors, "Media.StateChanged");
    }

    private sealed class Observer(CarMediaController controller) : IObserver<CarPlaybackState>
    {
        public void OnNext(CarPlaybackState value) => controller.Receive(value);

        public void OnError(Exception error) => controller._errors.HandleError(error, new CarErrorContext("Media.State", null));

        public void OnCompleted()
        {
        }
    }
}

// Kind of seekTo from Media3 (COMMAND_SEEK_TO_NEXT…, SEEK_TO_MEDIA_ITEM, SEEK_IN_CURRENT_MEDIA_ITEM)
internal enum CarMediaSeekKind
{
    InItem,
    Next,
    Previous,
    ToItem,
}
