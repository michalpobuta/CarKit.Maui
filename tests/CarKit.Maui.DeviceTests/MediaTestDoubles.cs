using CarKit.Maui.Media;
using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.DeviceTests;

// Test library (Stage 5): root → Albums (grid), Tracks (5 tracks), Playlists; search by title
public sealed class DeviceTestMediaLibrary : ICarMediaLibrary
{
    private static readonly Dictionary<string, CarMediaItem[]> _children = new()
    {
        ["root"] =
        [
            new("albums", "Albums") { IsBrowsable = true, ChildrenStyle = CarMediaChildrenStyle.Grid },
            new("tracks", "Tracks") { IsBrowsable = true },
            new("lists", "Playlists") { IsBrowsable = true },
        ],
        ["albums"] = [new("album1", "Album 1") { IsBrowsable = true, Artwork = CarImage.FromFile("carkit_test.png") }],
        ["tracks"] = [.. Enumerable.Range(1, 5).Select(Track)],
        ["lists"] = [Track(1), Track(2)],
    };

    public static CarMediaItem Root { get; } = new("root", "Library") { IsBrowsable = true };

    public static CarMediaItem Track(int number) => new("t" + number, "Track " + number)
    {
        IsPlayable = true,
        Subtitle = "Artist",
        Duration = TimeSpan.FromMinutes(3),
        IsExplicit = number == 2,
        Progress = number == 3 ? 0.5 : null,
    };

    public Task<CarMediaItem> GetRootAsync(CancellationToken ct) => Task.FromResult(Root);

    public Task<IReadOnlyList<CarMediaItem>> GetChildrenAsync(string parentId, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CarMediaItem>>(
            [.. (_children.TryGetValue(parentId, out var items) ? items : []).Skip(page * pageSize).Take(pageSize)]);

    // "fail" = a library error (review I7: Android Auto search must not wait forever)
    public Task<IReadOnlyList<CarMediaItem>> SearchAsync(string query, CancellationToken ct) =>
        query == "fail"
            ? Task.FromException<IReadOnlyList<CarMediaItem>>(new IOException("search failed"))
            : Task.FromResult<IReadOnlyList<CarMediaItem>>(
                [.. _children["tracks"].Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase))]);

    public Task<CarMediaItem?> GetItemAsync(string id, CancellationToken ct) =>
        Task.FromResult(_children.Values.SelectMany(i => i).FirstOrDefault(i => i.Id == id));
}

// Test player: records the commands, the test sets the state (Emit from any thread); no sound
public sealed class DeviceTestMediaPlayer : ICarMediaPlayer, IObservable<CarPlaybackState>
{
    private readonly List<IObserver<CarPlaybackState>> _observers = [];
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private CarPlaybackState _state = new();

    public IObservable<CarPlaybackState> State => this;

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

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

    public void Reset(CarPlaybackState? state = null)
    {
        lock (_lock)
        {
            _calls.Clear();
        }

        Emit(state ?? new CarPlaybackState());
    }

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
        return new Unsubscriber(this, observer);
    }

    public Task PlayAsync(string itemId) => Record("Play:" + itemId);

    public Task PauseAsync() => Record("Pause");

    public Task ResumeAsync() => Record("Resume");

    public Task SeekAsync(TimeSpan position) => Record("Seek:" + ((int)position.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));

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
            _calls.Add(call);
        }

        return Task.CompletedTask;
    }

    private sealed class Unsubscriber(DeviceTestMediaPlayer owner, IObserver<CarPlaybackState> observer) : IDisposable
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

internal static class MediaTestServices
{
    public static IServiceProvider Services => IPlatformApplication.Current!.Services;

    public static DeviceTestMediaPlayer Player => (DeviceTestMediaPlayer)Services.GetRequiredService<ICarMediaPlayer>();

    public static CarPlaybackState Playing(int number = 1, double seconds = 10) => new()
    {
        Status = CarPlaybackStatus.Playing,
        Item = DeviceTestMediaLibrary.Track(number),
        Position = TimeSpan.FromSeconds(seconds),
    };

    // Waits for the last player command (commands go through the controller and a Media3 future)
    public static Task WaitForCall(string call, int timeoutMs = 5000) =>
        Check.Eventually(() => Player.Calls.Contains(call), "player call " + call + " (calls: " + string.Join(", ", Player.Calls) + ")", timeoutMs);
}
