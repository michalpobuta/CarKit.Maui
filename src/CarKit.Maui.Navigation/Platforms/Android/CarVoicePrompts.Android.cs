using Android.Content;
using Android.Media;
using Android.OS;
using Android.Speech.Tts;
using TextToSpeech = Android.Speech.Tts.TextToSpeech;

namespace CarKit.Maui.Navigation;

// TextToSpeech with navigation audio attributes (spec: "Navigation module decisions (Stage 8)", Voice prompts): the
// engine is created at the first prompt, the task is completed by UtteranceProgressListener (end, error, stop). Main
// thread.
internal sealed class CarSpeechEngine(Context context) : Java.Lang.Object, ICarSpeechEngine, TextToSpeech.IOnInitListener
{
    private readonly Dictionary<string, TaskCompletionSource> _pending = [];
    private TaskCompletionSource<bool>? _ready;
    private TextToSpeech? _speech;
    private int _nextId;

    public async Task SpeakAsync(string text, string? language)
    {
        if (!await Ready())
        {
            Reset();
            throw new CarPlatformException("Text-to-speech is not available on this device.");
        }

        var speech = _speech!;
        var id = "carkit-" + ++_nextId;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        // Speak and SetLanguage are synchronous Binder calls to the engine process: off the main thread, because a hung
        // engine (on the emulator the TTS process crashes natively) would block the app UI (ANR)
        var result = await Task.Run(() =>
        {
            if (language is not null)
            {
                speech.SetLanguage(Java.Util.Locale.ForLanguageTag(language));
            }

            return speech.Speak(text, QueueMode.Flush, null, id);
        });
        if (result != OperationResult.Success)
        {
            _pending.Remove(id);
            // Engine disconnected (e.g. the TTS process died): the next prompt creates it anew
            Reset();
            throw new CarPlatformException("Text-to-speech rejected the voice prompt.");
        }

        await completion.Task;
    }

    public void Stop()
    {
        if (_speech is { } speech)
        {
            _ = Task.Run(() => speech.Stop());
        }
    }

    // Queue watchdog: callbacks of the old engine will not arrive, so the tasks are dropped and the engine is created
    // anew at the next prompt
    void ICarSpeechEngine.Reset()
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetCanceled();
        }

        _pending.Clear();
        Reset();
    }

    public void OnInit(OperationResult status) => _ready?.TrySetResult(status == OperationResult.Success);

    internal void Complete(string? id, Exception? error, bool cancelled)
    {
        if (id is null || !_pending.Remove(id, out var completion))
        {
            return;
        }

        if (cancelled)
        {
            completion.TrySetCanceled();
        }
        else if (error is not null)
        {
            completion.TrySetException(error);
        }
        else
        {
            completion.TrySetResult();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _speech?.Shutdown();
            _speech?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Reset()
    {
        var speech = _speech;
        _speech = null;
        _ready = null;
        if (speech is not null)
        {
            _ = Task.Run(() =>
            {
                speech.Shutdown();
                speech.Dispose();
            });
        }
    }

    private Task<bool> Ready()
    {
        if (_ready is null)
        {
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _speech = new TextToSpeech(context, this);
            _speech.SetAudioAttributes(CarAudioDucking.Attributes);
            _speech.SetOnUtteranceProgressListener(new Progress(this));
        }

        return _ready.Task;
    }

    // Engine callbacks arrive from the TTS thread: completion on the main thread
    private sealed class Progress(CarSpeechEngine engine) : UtteranceProgressListener
    {
        private static readonly Handler _main = new(Looper.MainLooper!);

        public override void OnStart(string? utteranceId)
        {
        }

        public override void OnDone(string? utteranceId) => _main.Post(() => engine.Complete(utteranceId, null, cancelled: false));

        [Obsolete("Required by the abstract base class; the new API is OnError(string, TextToSpeechError).")]
        public override void OnError(string? utteranceId) =>
            _main.Post(() => engine.Complete(utteranceId, new CarPlatformException("Text-to-speech failed to speak the voice prompt."), cancelled: false));

        public override void OnError(string? utteranceId, TextToSpeechError errorCode) =>
            _main.Post(() => engine.Complete(utteranceId, new CarPlatformException($"Text-to-speech failed to speak the voice prompt ({errorCode})."), cancelled: false));

        public override void OnStop(string? utteranceId, bool interrupted) => _main.Post(() => engine.Complete(utteranceId, null, cancelled: true));
    }
}

// Transient focus with ducking (Google: USAGE_ASSISTANCE_NAVIGATION_GUIDANCE + AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK) for
// the duration of a series of prompts; the Media module player gets CAN_DUCK (from API 26 the system ducks it)
internal sealed class CarAudioDucking(Context context) : Java.Lang.Object, ICarAudioDucking, AudioManager.IOnAudioFocusChangeListener
{
    private AudioFocusRequestClass? _request;

    public static AudioAttributes Attributes { get; } = new AudioAttributes.Builder()
        .SetUsage(AudioUsageKind.AssistanceNavigationGuidance)!
        .SetContentType(AudioContentType.Speech)!
        .Build()!;

    // Device tests: focus held for the series
    internal bool Held { get; private set; }

    private AudioManager Audio => (AudioManager)context.GetSystemService(Context.AudioService)!;

    public bool Begin()
    {
        AudioFocusRequest result;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _request ??= new AudioFocusRequestClass.Builder(AudioFocus.GainTransientMayDuck)
                .SetAudioAttributes(Attributes)!
                .SetOnAudioFocusChangeListener(this, new Handler(Looper.MainLooper!))!
                .Build()!;
            result = Audio.RequestAudioFocus(_request);
        }
        else
        {
#pragma warning disable CA1422, CS0618 // AudioFocusRequestClass requires API 26; this is the path for API 23–25
            result = Audio.RequestAudioFocus(this, Android.Media.Stream.Music, AudioFocus.GainTransientMayDuck);
#pragma warning restore CA1422, CS0618
        }

        Held = result == AudioFocusRequest.Granted;
        return Held;
    }

    public void End()
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

        Held = false;
    }

    // A prompt is short: losing focus in the middle (e.g. a call) does not interrupt it; the next one requests focus
    // again
    public void OnAudioFocusChange(AudioFocus focusChange)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _request?.Dispose();
        }

        base.Dispose(disposing);
    }
}
