using Android.Content;
using Android.Media;
using Android.OS;
using AndroidX.Core.Content;

namespace CarKit.Maui.Media;

// Android audio focus (spec: "Media module decisions (Stage 5)"): an AUDIOFOCUS_GAIN request (media, music) when the
// state changes to Playing/Buffering, release on Stopped/Ended/Idle/Error; focus changes and
// ACTION_AUDIO_BECOMING_NOISY → the controller's shared interruption policy. Ducking from API 26 is done by the system
// (no setWillPauseWhenDucked). Main thread.
internal sealed class CarAudioFocus : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
{
    private readonly CarMediaRuntime _runtime;
    private readonly ICarDispatcher _dispatcher;
    private readonly Context _context;
    private readonly NoisyReceiver _noisy;
    private readonly CarAudioFocusState _state = new();
    private AudioFocusRequestClass? _request;
    private bool _noisyRegistered;
    private bool _started;

    public CarAudioFocus(CarMediaRuntime runtime, ICarDispatcher dispatcher, Context context)
    {
        _runtime = runtime;
        _dispatcher = dispatcher;
        _context = context;
        _noisy = new NoisyReceiver(this);
    }

    internal bool IsHeld => _state.IsHeld;

    private CarMediaController Controller => _runtime.Controller;

    private AudioManager Audio => (AudioManager)_context.GetSystemService(Context.AudioService)!;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Controller.StateChanged += OnStateChanged;
        OnStateChanged(Controller.State);
    }

    // Called by AudioManager (main loop) and by device tests
    public void OnAudioFocusChange(AudioFocus focusChange) => Run(() =>
    {
        switch (focusChange)
        {
            case AudioFocus.Gain:
                _state.OnGain();
                return Controller.Interruptions.FocusGainedAsync();
            case AudioFocus.LossTransient:
            case AudioFocus.LossTransientCanDuck:
                _state.OnTransientLoss();
                return Controller.Interruptions.FocusLostAsync(transient: true);
            case AudioFocus.Loss:
                Abandon();
                return Controller.Interruptions.FocusLostAsync(transient: false);
            default:
                return Task.CompletedTask;
        }
    }, "Media.AudioFocus");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Controller.StateChanged -= OnStateChanged;
            Abandon();
            UnregisterNoisy();
            _noisy.Dispose();
            _request?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnStateChanged(CarPlaybackState _)
    {
        var state = Controller.State;
        if (state.IsActive)
        {
            RegisterNoisy();
        }
        else
        {
            UnregisterNoisy();
        }

        switch (_state.OnPlaybackState(state, Controller.Interruptions.ResumePending))
        {
            case CarAudioFocusAction.Request:
                Request();
                break;
            case CarAudioFocusAction.Abandon:
                AbandonRequest();
                break;
        }
    }

    private void Request()
    {
        AudioFocusRequest result;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _request ??= new AudioFocusRequestClass.Builder(AudioFocus.Gain)
                .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!)!
                .SetOnAudioFocusChangeListener(this, new Handler(Looper.MainLooper!))!
                .Build()!;
            result = Audio.RequestAudioFocus(_request);
        }
        else
        {
#pragma warning disable CA1422, CS0618 // AudioFocusRequestClass requires API 26; this is the path for API 23–25
            result = Audio.RequestAudioFocus(this, Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422, CS0618
        }

        _state.OnRequestResult(granted: result == AudioFocusRequest.Granted);
        if (result == AudioFocusRequest.Granted)
        {
            return;
        }

        // Denied (e.g. a call is in progress): playing without focus would drown out other apps
        Run(Controller.PauseAsync, "Media.AudioFocus");
    }

    private void Abandon()
    {
        if (!_state.Requested)
        {
            return;
        }

        _state.OnAbandoned();
        AbandonRequest();
    }

    private void AbandonRequest()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (_request is not null)
            {
                Audio.AbandonAudioFocusRequest(_request);
            }
        }
        else
        {
#pragma warning disable CA1422, CS0618 // AbandonAudioFocusRequest requires API 26; this is the path for API 23–25
            Audio.AbandonAudioFocus(this);
#pragma warning restore CA1422, CS0618
        }
    }

    private void RegisterNoisy()
    {
        if (!_noisyRegistered)
        {
            ContextCompat.RegisterReceiver(_context, _noisy, new IntentFilter(AudioManager.ActionAudioBecomingNoisy), ContextCompat.ReceiverNotExported);
            _noisyRegistered = true;
        }
    }

    private void UnregisterNoisy()
    {
        if (_noisyRegistered)
        {
            _context.UnregisterReceiver(_noisy);
            _noisyRegistered = false;
        }
    }

    private void Run(Func<Task> work, string operation) => _dispatcher.DispatchAsync(work).Forget(_runtime.Errors, operation);

    // Headphones/Bluetooth disconnected: pause without resume
    private sealed class NoisyReceiver(CarAudioFocus owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
            {
                owner.Run(owner.Controller.Interruptions.OutputDisconnectedAsync, "Media.BecomingNoisy");
            }
        }
    }
}
