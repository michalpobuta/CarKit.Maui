using AVFoundation;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Navigation;

// AVSpeechSynthesizer on the app audio session (spec: "Navigation module decisions (Stage 8)", Voice prompts). Main
// thread; the task is completed by the synthesizer delegate (end or cancellation after Stop).
internal sealed class CarSpeechEngine : ICarSpeechEngine, IDisposable
{
    private readonly AVSpeechSynthesizer _synthesizer = new();
    private readonly SynthesizerDelegate _delegate;

    public CarSpeechEngine()
    {
        _delegate = new SynthesizerDelegate();
        _synthesizer.Delegate = _delegate;
        // Voice on the app session (ducking is set by CarAudioDucking), not on a separate system session
        _synthesizer.UsesApplicationAudioSession = true;
    }

    public Task SpeakAsync(string text, string? language)
    {
        var utterance = new AVSpeechUtterance(text);
        if (language is not null && AVSpeechSynthesisVoice.FromLanguage(language) is { } voice)
        {
            utterance.Voice = voice;
        }

        var completion = _delegate.Expect(utterance);
        _synthesizer.SpeakUtterance(utterance);
        return completion;
    }

    public void Stop() => _synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);

    // Queue watchdog: the synthesizer did not report the end; pending tasks are dropped
    public void Reset() => _delegate.CancelAll();

    public void Dispose()
    {
        _synthesizer.Dispose();
        _delegate.Dispose();
    }

    private sealed class SynthesizerDelegate : AVSpeechSynthesizerDelegate
    {
        private readonly Dictionary<AVSpeechUtterance, TaskCompletionSource> _pending = new(ReferenceEqualityComparer.Instance);

        public void CancelAll()
        {
            foreach (var completion in _pending.Values)
            {
                completion.TrySetCanceled();
            }

            _pending.Clear();
        }

        public Task Expect(AVSpeechUtterance utterance)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[utterance] = completion;
            return completion.Task;
        }

        public override void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance)
        {
            if (_pending.Remove(utterance, out var completion))
            {
                completion.TrySetResult();
            }
        }

        public override void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance)
        {
            if (_pending.Remove(utterance, out var completion))
            {
                completion.TrySetCanceled();
            }
        }
    }
}

// Ducking for the duration of a series of prompts: Playback + VoicePrompt mode + DuckOthers |
// InterruptSpokenAudioAndMixWithOthers, then restoring the previous category and deactivation with
// NotifyOthersOnDeactivation (other apps return to normal volume). When the Media module is playing
// (CarAudioSessionCoordinator), it does not change the session: the prompt mixes with the app playback.
internal sealed class CarAudioDucking(ILogger logger) : ICarAudioDucking
{
    private (string Category, string Mode, AVAudioSessionCategoryOptions Options)? _previous;

    // Device tests: whether the series changed the session
    internal bool Changed => _previous is not null;

    public bool Begin()
    {
        _previous = null;
        if (CarAudioSessionCoordinator.MediaActive)
        {
            return true;
        }

        var session = AVAudioSession.SharedInstance();
        var previous = (session.Category.ToString(), session.Mode.ToString(), session.CategoryOptions);
        var options = AVAudioSessionCategoryOptions.DuckOthers | AVAudioSessionCategoryOptions.InterruptSpokenAudioAndMixWithOthers;
        if (session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionMode.VoicePrompt, options) is { } error)
        {
            CarNavigationLog.Trace(logger, "AVAudioSession rejected the voice prompt category: " + error.LocalizedDescription);
            return false;
        }

        _previous = previous;
        if (session.SetActive(true) is { } activation)
        {
            CarNavigationLog.Trace(logger, "AVAudioSession did not activate for the voice prompt: " + activation.LocalizedDescription);
            Restore(session);
            return false;
        }

        return true;
    }

    public void End()
    {
        if (_previous is null)
        {
            return;
        }

        // M10 (review): the Media module started playing during the series: its category and active session stay
        if (CarAudioSessionCoordinator.MediaActive)
        {
            _previous = null;
            return;
        }

        var session = AVAudioSession.SharedInstance();
        session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
        Restore(session);
    }

    private void Restore(AVAudioSession session)
    {
        if (_previous is { } previous)
        {
            session.SetCategory(previous.Category, previous.Mode, previous.Options, out _);
        }

        _previous = null;
    }
}
