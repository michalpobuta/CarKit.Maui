using Android.Util;
using AndroidX.Media3.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace CarKit.Maui.Media;

/// <summary>
/// The Media3 media library service Android Auto browses and controls. The CarKit source generator derives
/// <c>CarKitGeneratedMediaLibraryService</c> from it in apps with the <c>Audio</c> category, with the intent filters
/// and the <c>mediaPlayback</c> foreground service type.
/// </summary>
/// <remarks>
/// The service maps <see cref="ICarMediaLibrary"/> to the Media3 item tree and <see cref="ICarMediaPlayer"/> to the
/// session player. Media3 shows the playback notification and keeps the service in the foreground while playing.
/// </remarks>
public abstract class CarKitMediaLibraryService : MediaLibraryService
{
    private MediaLibrarySession? _session;
    private CarKitPlayer? _player;
    private CarMediaLibraryCallback? _callback;
    private CarMediaRuntime? _runtime;
    private CancellationTokenSource? _lifetime;

    /// <summary>Creates the service; Android creates it when a media controller binds, also without an activity.</summary>
    protected CarKitMediaLibraryService()
    {
    }

    /// <summary>Creates the media session from the CarKit services of the app.</summary>
    public override void OnCreate()
    {
        base.OnCreate();
        try
        {
            // MauiApplication.OnCreate created the MauiApp before Android created the service (also without an
            // Activity)
            var services = (IServiceProvider?)IPlatformApplication.Current?.Services
                ?? throw new InvalidOperationException("CarKit: MauiApp has not been created yet (IPlatformApplication.Current is null).");
            _runtime = services.GetService<CarMediaRuntime>()
                ?? throw new InvalidOperationException("CarKit media is not registered: call car.UseMedia(...) inside builder.UseCarKit(...).");
        }
        catch (InvalidOperationException ex)
        {
            // An exception from a service callback would crash the process; without a session Media3 controllers
            // get a connection refusal
            Log.Error("CarKit", ex.Message);
            return;
        }

        _lifetime = new CancellationTokenSource();
        _player = new CarKitPlayer(_runtime, this);
        _callback = new CarMediaLibraryCallback(_runtime, this) { Cancellation = _lifetime.Token };
        _session = new MediaLibrarySession.Builder(this, _player, _callback).SetId("carkit")!.Build()!;
        _runtime.Controller.StateChanged += OnStateChanged;
        // Session buttons right away (also when playback was in progress before the service was created): legacy
        // controllers (Android Auto) see them as custom PlaybackState actions
        OnStateChanged(_runtime.Controller.State);
        CarMediaSessions.Opened(_session);
        CarMediaLog.Info(_runtime.Logger, "media library session created");
    }

    /// <summary>Returns the media library session for every controller (Android Auto, the notification, the app).</summary>
    /// <param name="p0">The controller.</param>
    /// <returns>The session, or <see langword="null"/> when CarKit is not configured.</returns>
    public override MediaLibrarySession? OnGetSessionFromMediaLibraryService(MediaSession.ControllerInfo? p0) => _session;

    // Binding also requires the base class overload; ACW exports only onGetSession from MediaLibrarySession
    /// <summary>Returns the media library session.</summary>
    /// <param name="p0">The controller.</param>
    /// <returns>The session, or <see langword="null"/> when CarKit is not configured.</returns>
    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? p0) => _session;

    /// <summary>Releases the session and the player.</summary>
    public override void OnDestroy()
    {
        if (_runtime is not null)
        {
            _runtime.Controller.StateChanged -= OnStateChanged;
        }

        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        if (_session is not null)
        {
            CarMediaSessions.Closed(_session);
            _session.Release();
            _session = null;
        }

        _player?.Release();
        _player = null;
        base.OnDestroy();
    }

    // Shuffle/Repeat icons follow the state; button preferences are sent only on change
    private List<AndroidX.Media3.Session.CommandButton>? _shownButtons;
    private IReadOnlyList<CarResolvedNowPlayingButton>? _shownResolved;

    // The controller's current state, not the argument: a subscriber earlier in the chain may have already invoked a
    // command and changed the state
    private void OnStateChanged(CarPlaybackState _)
    {
        if (_session is null || _callback is null || _runtime is null)
        {
            return;
        }

        var state = _runtime.Controller.State;

        var resolved = CarNowPlayingButtons.Resolve(_runtime.Options.NowPlaying, state);
        if (_shownResolved is { } shown && shown.SequenceEqual(resolved))
        {
            return;
        }

        _shownResolved = resolved;
        _shownButtons = _callback.Buttons(state);
        _session.SetMediaButtonPreferences(_shownButtons);
    }
}
