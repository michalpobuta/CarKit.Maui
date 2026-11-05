using CarKit.Maui.Media;

namespace CarKit.Maui.Tests.Media;

// Test player: records the commands, the test emits the state (Emit), also from another thread
internal sealed class TestMediaPlayer : ICarMediaPlayer, IObservable<CarPlaybackState>
{
    private readonly List<IObserver<CarPlaybackState>> _observers = [];
    private readonly Lock _lock = new();
    private CarPlaybackState _state = new();

    public List<string> Calls { get; } = [];

    public Exception? FailWith { get; set; }

    public IObservable<CarPlaybackState> State => this;

    public void Emit(CarPlaybackState state)
    {
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

    public IDisposable Subscribe(IObserver<CarPlaybackState> observer)
    {
        CarPlaybackState current;
        lock (_lock)
        {
            _observers.Add(observer);
            current = _state;
        }

        observer.OnNext(current);
        return new Unsubscriber(() =>
        {
            lock (_lock)
            {
                _observers.Remove(observer);
            }
        });
    }

    public Task PlayAsync(string itemId) => Record("Play:" + itemId);

    public Task PauseAsync() => Record("Pause");

    public Task ResumeAsync() => Record("Resume");

    public Task SeekAsync(TimeSpan position) => Record("Seek:" + position.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task SkipNextAsync() => Record("SkipNext");

    public Task SkipPreviousAsync() => Record("SkipPrevious");

    public Task SetShuffleAsync(bool on) => Record("Shuffle:" + on);

    public Task SetRepeatAsync(CarRepeatMode mode) => Record("Repeat:" + mode);

    public Task SetRateAsync(float rate) => Record("Rate:" + rate.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public Task InvokeCustomActionAsync(string actionId) => Record("Custom:" + actionId);

    private Task Record(string call)
    {
        lock (_lock)
        {
            Calls.Add(call);
        }

        return FailWith is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

// Test library: a tree in a dictionary (parent → children), a call log, an optional error
internal sealed class TestMediaLibrary : ICarMediaLibrary
{
    public CarMediaItem Root { get; set; } = new("root", "Library") { IsBrowsable = true };

    public Dictionary<string, List<CarMediaItem>> Children { get; } = [];

    public Dictionary<string, CarMediaItem> Items { get; } = [];

    public List<string> Calls { get; } = [];

    public Exception? FailWith { get; set; }

    // Asynchronous response (Task.Yield) and a record of whether the call arrived on the main thread (test I8)
    public bool Yield { get; set; }

    public Func<bool>? MainThreadCheck { get; set; }

    public List<bool> ThreadChecks { get; } = [];

    public TestMediaLibrary Add(string parentId, params CarMediaItem[] items)
    {
        if (!Children.TryGetValue(parentId, out var list))
        {
            Children[parentId] = list = [];
        }

        list.AddRange(items);
        foreach (var item in items)
        {
            Items[item.Id] = item;
        }

        return this;
    }

    public static CarMediaItem Folder(string id, string title) => new(id, title) { IsBrowsable = true };

    public static CarMediaItem Track(string id, string title) => new(id, title) { IsPlayable = true, Subtitle = "Artist", Duration = TimeSpan.FromMinutes(3) };

    public async Task<CarMediaItem> GetRootAsync(CancellationToken ct)
    {
        Calls.Add("Root");
        if (MainThreadCheck is { } check)
        {
            ThreadChecks.Add(check());
        }

        if (Yield)
        {
            await Task.Delay(1, ct).ConfigureAwait(false);
        }

        return FailWith is { } error ? throw error : Root;
    }

    public async Task<IReadOnlyList<CarMediaItem>> GetChildrenAsync(string parentId, int page, int pageSize, CancellationToken ct)
    {
        Calls.Add($"Children:{parentId}:{page}:{pageSize}");
        if (MainThreadCheck is { } check)
        {
            ThreadChecks.Add(check());
        }

        if (Yield)
        {
            await Task.Delay(1, ct).ConfigureAwait(false);
        }

        if (FailWith is { } error)
        {
            throw error;
        }

        var all = Children.TryGetValue(parentId, out var list) ? list : [];
        return [.. all.Skip(page * pageSize).Take(pageSize)];
    }

    public Task<IReadOnlyList<CarMediaItem>> SearchAsync(string query, CancellationToken ct)
    {
        Calls.Add("Search:" + query);
        return Task.FromResult<IReadOnlyList<CarMediaItem>>(
            [.. Items.Values.Where(i => i.IsPlayable && i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(i => i.Id, StringComparer.Ordinal)]);
    }

    public Task<CarMediaItem?> GetItemAsync(string id, CancellationToken ct) => Task.FromResult(Items.GetValueOrDefault(id));
}

// Dispatcher "always on the main thread" (logic tests without thread hops)
internal sealed class InlineDispatcher : ICarDispatcher
{
    public bool IsDispatchRequired => false;

    public Task DispatchAsync(Func<Task> work) => work();

    public Task<T> DispatchAsync<T>(Func<Task<T>> work) => work();
}

// Dispatcher with a manually pumped queue: OnMainThread decides whether the caller is "on the main thread"
internal sealed class ManualDispatcher : ICarDispatcher
{
    private readonly Queue<Func<Task>> _queue = new();

    public bool OnMainThread { get; set; } = true;

    public bool IsDispatchRequired => !OnMainThread;

    public Task DispatchAsync(Func<Task> work)
    {
        if (OnMainThread)
        {
            return work();
        }

        _queue.Enqueue(work);
        return Task.CompletedTask;
    }

    public Task<T> DispatchAsync<T>(Func<Task<T>> work) => OnMainThread ? work() : throw new NotSupportedException();

    public void Pump()
    {
        OnMainThread = true;
        while (_queue.TryDequeue(out var work))
        {
            work();
        }
    }
}

// Manually driven clock (playback position)
internal sealed class ManualTime : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

internal sealed class RecordingErrors : ICarErrorHandler
{
    public List<(Exception Exception, CarErrorContext Context)> Errors { get; } = [];

    public void HandleError(Exception exception, CarErrorContext context)
    {
        lock (Errors)
        {
            Errors.Add((exception, context));
        }
    }
}

internal static class TestMediaFactory
{
    public static CarMediaOptions Options(params float[] rates) =>
        new(rates.Length == 0 ? [1f] : rates, CarNowPlayingButtonsAccess.Default(rates.Length == 0 ? [1f] : rates), AutoBrowse: true, TemplatedMedia: false);

    public static (CarMediaController Controller, TestMediaPlayer Player, ManualTime Time, RecordingErrors Errors) Controller(params float[] rates)
    {
        var player = new TestMediaPlayer();
        var time = new ManualTime();
        var errors = new RecordingErrors();
        return (new CarMediaController(player, Options(rates), errors, new InlineDispatcher(), time), player, time, errors);
    }

    public static CarPlaybackState Playing(string id = "t1", double seconds = 10) =>
        new() { Status = CarPlaybackStatus.Playing, Item = TestMediaLibrary.Track(id, "Track " + id), Position = TimeSpan.FromSeconds(seconds) };
}

internal static class CarNowPlayingButtonsAccess
{
    public static CarNowPlayingTemplate Default(IReadOnlyList<float> rates) => CarNowPlayingButtons.Default(rates);
}
