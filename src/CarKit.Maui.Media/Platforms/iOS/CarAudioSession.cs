using AVFoundation;
using Foundation;

namespace CarKit.Maui.Media;

// AVAudioSession (spec: "Media module decisions (Stage 5)"): Playback category from start, activation on playback,
// interruptions (call: Began → pause, Ended with ShouldResume → resume) and route change (OldDeviceUnavailable →
// pause) through the controller's shared policy. Notifications may arrive from another thread; the policy runs on the
// main thread.
internal sealed class CarAudioSession(CarMediaRuntime runtime, ICarDispatcher dispatcher) : IDisposable
{
    private NSObject? _interruption;
    private NSObject? _routeChange;
    private bool _active;

    private CarMediaController Controller => runtime.Controller;

    public void Start()
    {
        if (_interruption is not null)
        {
            return;
        }

        var session = AVAudioSession.SharedInstance();
        if (session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.AllowAirPlay | AVAudioSessionCategoryOptions.AllowBluetoothA2DP) is { } error)
        {
            CarMediaLog.Warning(runtime.Logger, "AVAudioSession rejected the Playback category: " + error.LocalizedDescription, null);
        }

        _interruption = AVAudioSession.Notifications.ObserveInterruption((_, e) =>
            OnInterruption(e.InterruptionType, e.Option.HasFlag(AVAudioSessionInterruptionOptions.ShouldResume)));
        _routeChange = AVAudioSession.Notifications.ObserveRouteChange((_, e) => OnRouteChange(e.Reason));
        Controller.StateChanged += OnStateChanged;
        OnStateChanged(Controller.State);
    }

    // Also from device tests through a posted notification (InterruptionNotification with type and option)
    internal void OnInterruption(AVAudioSessionInterruptionType type, bool shouldResume) => Run(() =>
    {
        if (type == AVAudioSessionInterruptionType.Began)
        {
            // The system deactivated the session; the next Playing activates it again
            _active = false;
            return Controller.Interruptions.BeganAsync();
        }

        return Controller.Interruptions.EndedAsync(shouldResume);
    }, "Media.Interruption");

    internal void OnRouteChange(AVAudioSessionRouteChangeReason reason)
    {
        if (reason == AVAudioSessionRouteChangeReason.OldDeviceUnavailable)
        {
            Run(Controller.Interruptions.OutputDisconnectedAsync, "Media.RouteChange");
        }
    }

    public void Dispose()
    {
        Controller.StateChanged -= OnStateChanged;
        _interruption?.Dispose();
        _routeChange?.Dispose();
        _interruption = null;
        _routeChange = null;
    }

    private void OnStateChanged(CarPlaybackState _)
    {
        // Stage 8: navigation voice prompts do not reconfigure a session that is playing (CarAudioSessionCoordinator in
        // CarKit.Maui)
        CarAudioSessionCoordinator.MediaActive = Controller.State.IsActive;
        if (!Controller.State.IsActive || _active)
        {
            return;
        }

        if (AVAudioSession.SharedInstance().SetActive(true) is { } error)
        {
            CarMediaLog.Warning(runtime.Logger, "AVAudioSession did not activate: " + error.LocalizedDescription, null);
            return;
        }

        _active = true;
    }

    private void Run(Func<Task> work, string operation) =>
        dispatcher.DispatchAsync(work).Forget(runtime.Errors, operation);
}
