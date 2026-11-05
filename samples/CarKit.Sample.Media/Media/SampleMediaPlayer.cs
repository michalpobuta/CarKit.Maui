using CarKit.Maui;
using CarKit.Maui.Media;
using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Media;

// Sample player: a queue (album/playlist), shuffle, repeat, rate and state for CarKit (ICarMediaPlayer.State),
// the sound is played by the platform's ISampleAudioOutput. Commands arrive from different threads (CarPlay, Media3,
// auto-browse after ConfigureAwait(false), the phone screen), so each goes to the main thread through ICarDispatcher
// (AVAudioPlayer, MediaPlayer).
public sealed class SampleMediaPlayer : ICarMediaPlayer, IObservable<CarPlaybackState>, IDisposable
{
    private readonly SampleMediaLibrary _library;
    private readonly ISampleAudioOutput _output;
    private readonly ICarDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly List<IObserver<CarPlaybackState>> _observers = [];
    private readonly Random _random;
    private List<CarMediaItem> _queue = [];
    private List<CarMediaItem> _ordered = [];
    private int _index = -1;
    private CarPlaybackStatus _status = CarPlaybackStatus.Idle;
    private bool _shuffle;
    private CarRepeatMode _repeat;
    private float _rate = 1f;
    private CarPlaybackState _state = new();

    public SampleMediaPlayer(SampleMediaLibrary library, ISampleAudioOutput output, ICarDispatcher dispatcher, ILogger<SampleMediaPlayer> logger)
        : this(library, output, dispatcher, logger, new Random())
    {
    }

    // Tests: predictable shuffling
    internal SampleMediaPlayer(SampleMediaLibrary library, ISampleAudioOutput output, ICarDispatcher dispatcher, ILogger logger, Random random)
    {
        _library = library;
        _output = output;
        _dispatcher = dispatcher;
        _logger = logger;
        _random = random;
        _output.Completed += OnCompleted;
    }

    public IObservable<CarPlaybackState> State => this;

    public CarPlaybackState Current
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public HashSet<string> Liked { get; } = [];

    public Task PlayAsync(string itemId) => Run(() =>
    {
        _ordered = [.. _library.QueueFor(itemId)];
        _queue = _shuffle ? Shuffled(_ordered, itemId) : [.. _ordered];
        _index = Math.Max(0, _queue.FindIndex(i => i.Id == itemId));
        if (_queue.Count > 0)
        {
            Start();
        }
    });

    public Task PauseAsync() => Run(() =>
    {
        if (_status is CarPlaybackStatus.Playing or CarPlaybackStatus.Buffering)
        {
            _output.Pause();
            _status = CarPlaybackStatus.Paused;
            Publish();
        }
    });

    public Task ResumeAsync() => Run(() =>
    {
        if (_index < 0)
        {
            return;
        }

        if (_status is CarPlaybackStatus.Ended or CarPlaybackStatus.Stopped)
        {
            Start();
            return;
        }

        _output.Play();
        _status = CarPlaybackStatus.Playing;
        Publish();
    });

    public Task SeekAsync(TimeSpan position) => Run(() =>
    {
        _output.Seek(position);
        Publish();
    });

    public Task SkipNextAsync() => Run(() =>
    {
        if (_index < 0)
        {
            return;
        }

        _index = _index + 1 < _queue.Count ? _index + 1 : 0;
        Start();
    });

    // As in players: after 3 s it goes to the start of the track, earlier to the previous one
    public Task SkipPreviousAsync() => Run(() =>
    {
        if (_index < 0)
        {
            return;
        }

        if (_output.Position > TimeSpan.FromSeconds(3))
        {
            _output.Seek(TimeSpan.Zero);
            Publish();
            return;
        }

        _index = _index > 0 ? _index - 1 : _queue.Count - 1;
        Start();
    });

    public Task SetShuffleAsync(bool on) => Run(() =>
    {
        _shuffle = on;
        if (_index >= 0)
        {
            var current = _queue[_index].Id;
            _queue = on ? Shuffled(_ordered, current) : [.. _ordered];
            _index = _queue.FindIndex(i => i.Id == current);
        }

        Publish();
    });

    public Task SetRepeatAsync(CarRepeatMode mode) => Run(() =>
    {
        _repeat = mode;
        Publish();
    });

    public Task SetRateAsync(float rate) => Run(() =>
    {
        _rate = rate;
        _output.SetRate(rate);
        Publish();
    });

    // The "Like" button from Now Playing (CarPlay: an image button; Android Auto: a Media3 session button)
    public Task InvokeCustomActionAsync(string actionId) => Run(() =>
    {
        if (actionId == "like" && _index >= 0)
        {
            var id = _queue[_index].Id;
            if (!Liked.Remove(id))
            {
                Liked.Add(id);
            }

            SampleMediaLog.Liked(_logger, id, Liked.Contains(id));
        }
    });

    public IDisposable Subscribe(IObserver<CarPlaybackState> observer)
    {
        CarPlaybackState current;
        lock (_lock)
        {
            _observers.Add(observer);
            current = _state;
        }

        observer.OnNext(current);
        return new Subscription(this, observer);
    }

    public void Dispose() => _output.Completed -= OnCompleted;

    // Audio output position log (the phone screen, every second): proof that the sound is actually playing
    public void LogPosition() => SampleMediaLog.Position(_logger, Current.Status, Current.Item?.Title, _output.Position.TotalSeconds, _output.IsPlaying);

    private Task Run(Action command) => _dispatcher.DispatchAsync(() =>
    {
        command();
        return Task.CompletedTask;
    });

    private static List<CarMediaItem> Shuffled(List<CarMediaItem> items, string first, Random random)
    {
        var rest = items.Where(i => i.Id != first).OrderBy(_ => random.Next()).ToList();
        return [.. items.Where(i => i.Id == first).Take(1), .. rest];
    }

    private List<CarMediaItem> Shuffled(List<CarMediaItem> items, string first) => Shuffled(items, first, _random);

    private void Start()
    {
        var item = _queue[_index];
        _output.Load(_library.AssetFor(item.Id));
        _output.SetRate(_rate);
        _output.Play();
        _status = CarPlaybackStatus.Playing;
        SampleMediaLog.Playing(_logger, item.Title, _library.AssetFor(item.Id));
        Publish();
    }

    // End of a track: repeat One = the same one, All = the next one with wrap-around, Off = the next one or the end of
    // the queue
    private void OnCompleted(object? sender, EventArgs e) => _ = Run(() =>
    {
        if (_repeat == CarRepeatMode.One)
        {
            Start();
        }
        else if (_index + 1 < _queue.Count || _repeat == CarRepeatMode.All)
        {
            _index = (_index + 1) % _queue.Count;
            Start();
        }
        else
        {
            _status = CarPlaybackStatus.Ended;
            Publish();
        }
    });

    private void Publish()
    {
        var item = _index >= 0 && _index < _queue.Count ? _queue[_index] : null;
        var state = new CarPlaybackState
        {
            Status = item is null ? CarPlaybackStatus.Idle : _status,
            Item = item is null ? null : item with { IsPlaying = _status == CarPlaybackStatus.Playing },
            Position = _output.Position,
            Duration = item?.Duration,
            Rate = _rate,
            IsShuffleEnabled = _shuffle,
            RepeatMode = _repeat,
            Queue = [.. _queue],
            QueueIndex = _index,
            CanSkipNext = _queue.Count > 1,
            CanSkipPrevious = item is not null,
        };
        IObserver<CarPlaybackState>[] observers;
        lock (_lock)
        {
            _state = state;
            observers = [.. _observers];
        }

        foreach (var observer in observers)
        {
            observer.OnNext(state);
        }
    }

    private sealed class Subscription(SampleMediaPlayer owner, IObserver<CarPlaybackState> observer) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._lock)
            {
                owner._observers.Remove(observer);
            }
        }
    }
}

internal static partial class SampleMediaLog
{
    [LoggerMessage(1, LogLevel.Information, "SampleMedia: playing {Title} ({Asset})")]
    public static partial void Playing(ILogger logger, string title, string asset);

    [LoggerMessage(2, LogLevel.Information, "SampleMedia: {Id} liked={Liked}")]
    public static partial void Liked(ILogger logger, string id, bool liked);

    [LoggerMessage(3, LogLevel.Information, "SampleMedia: {Status} {Title} at {Position:F1} s (output playing={OutputPlaying})")]
    public static partial void Position(ILogger logger, CarPlaybackStatus status, string? title, double position, bool outputPlaying);
}
