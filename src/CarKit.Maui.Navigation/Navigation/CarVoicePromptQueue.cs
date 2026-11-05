namespace CarKit.Maui.Navigation;

// Platform speech engine (iOS AVSpeechSynthesizer, Android TextToSpeech). Main thread. SpeakAsync completes after the
// utterance, by cancellation after Stop() or by an engine error.
internal interface ICarSpeechEngine
{
    Task SpeakAsync(string text, string? language);

    void Stop();

    // The engine did not report the end of a prompt (queue watchdog): pending tasks are dropped, the next prompt
    // creates the engine anew (Android: the TTS process died and will not answer)
    void Reset();
}

// Focus and ducking while the queue is non-empty (iOS AVAudioSession, Android AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK).
// Main thread.
internal interface ICarAudioDucking
{
    // false = the system refused (e.g. a phone call): the prompt will not sound
    bool Begin();

    void End();
}

// Voice prompt queue (spec: "Navigation module decisions (Stage 8)", Voice prompts): FIFO, one prompt at a time,
// ducking from the first to the last prompt of a series; cancellation by token (queued or in progress), CancelAll and
// the car ending navigation. Engine and ducking on the main thread (dispatcher).
internal sealed class CarVoicePromptQueue : ICarVoicePrompts
{
    private readonly ICarSpeechEngine _engine;
    private readonly ICarAudioDucking _audio;
    private readonly ICarDispatcher _dispatcher;
    private readonly ICarErrorHandler _errors;
    private readonly string? _language;
    private readonly Func<string, TimeSpan> _watchdog;
    private readonly Lock _lock = new();
    private readonly LinkedList<Prompt> _queue = [];
    private Prompt? _current;
    private bool _running;
    private bool _ducked;

    public CarVoicePromptQueue(
        ICarSpeechEngine engine, ICarAudioDucking ducking, ICarDispatcher dispatcher, ICarErrorHandler errors, string? language, Func<string, TimeSpan>? watchdog = null)
    {
        _watchdog = watchdog ?? Watchdog;
        _engine = engine;
        _audio = ducking;
        _dispatcher = dispatcher;
        _errors = errors;
        _language = language;
    }

    public bool IsSpeaking
    {
        get
        {
            lock (_lock)
            {
                return _current is not null;
            }
        }
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = new Prompt(text);
        bool start;
        lock (_lock)
        {
            prompt.Node = _queue.AddLast(prompt);
            start = !_running;
            _running = true;
        }

        if (cancellationToken.CanBeCanceled)
        {
            prompt.Registration = cancellationToken.Register(() => Cancel(prompt));
        }

        if (start)
        {
            _dispatcher.DispatchAsync(DrainAsync).Forget(_errors, "Navigation.VoicePrompt");
        }

        return prompt.Completion.Task;
    }

    public void CancelAll()
    {
        Prompt[] pending;
        Prompt? current;
        lock (_lock)
        {
            pending = [.. _queue];
            _queue.Clear();
            current = _current;
        }

        foreach (var prompt in pending)
        {
            prompt.Node = null;
            prompt.Cancel();
        }

        if (current is not null)
        {
            current.Cancel();
            Stop();
        }
    }

    private void Cancel(Prompt prompt)
    {
        bool speaking;
        lock (_lock)
        {
            if (prompt.Node is { } node)
            {
                _queue.Remove(node);
                prompt.Node = null;
            }

            speaking = ReferenceEquals(_current, prompt);
        }

        prompt.Cancel();
        if (speaking)
        {
            Stop();
        }
    }

    private void Stop() =>
        _dispatcher.DispatchAsync(() =>
        {
            _engine.Stop();
            return Task.CompletedTask;
        }).Forget(_errors, "Navigation.VoicePrompt");

    // Main thread: successive prompts, ducking through the whole series
    private async Task DrainAsync()
    {
        while (true)
        {
            Prompt? prompt;
            lock (_lock)
            {
                if (_queue.First is not { } first)
                {
                    _current = null;
                    _running = false;
                    break;
                }

                prompt = first.Value;
                _queue.RemoveFirst();
                prompt.Node = null;
                _current = prompt;
            }

            if (prompt.Completion.Task.IsCompleted)
            {
                continue;
            }

            if (!_ducked && !(_ducked = BeginDucking(prompt)))
            {
                continue;
            }

            try
            {
                var speaking = _engine.SpeakAsync(prompt.Text, _language);
                using var timer = new CancellationTokenSource();
                var finished = await Task.WhenAny(speaking, Task.Delay(_watchdog(prompt.Text), timer.Token));
                await timer.CancelAsync();
                if (finished != speaking)
                {
                    // The engine did not report the end (e.g. the TTS engine process died): a prompt must not block
                    // the queue
                    _engine.Stop();
                    _engine.Reset();
                    _ = speaking.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    throw new CarPlatformException("The speech engine did not finish the voice prompt in time.");
                }

                await speaking;
                prompt.Complete();
            }
            catch (OperationCanceledException)
            {
                prompt.Cancel();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                prompt.Fail(ex as CarPlatformException ?? new CarPlatformException("The speech engine failed: " + ex.Message, ex));
            }
        }

        if (_ducked)
        {
            _ducked = false;
            _audio.End();
        }
    }

    // A platform exception (e.g. AVAudioSession) must not stop the loop: _running would stay true and the queue would
    // stall (review M9)
    private bool BeginDucking(Prompt prompt)
    {
        try
        {
            if (_audio.Begin())
            {
                return true;
            }

            prompt.Fail(new CarPlatformException("The car did not grant audio focus for the voice prompt."));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            prompt.Fail(new CarPlatformException("The audio session for the voice prompt failed: " + ex.Message, ex));
        }

        return false;
    }

    // Per-prompt limit: 8 s + 100 ms per character (a navigation prompt is a few seconds of speech)
    internal static TimeSpan Watchdog(string text) => TimeSpan.FromSeconds(8) + TimeSpan.FromMilliseconds(100 * text.Length);

    private sealed class Prompt(string text)
    {
        public string Text { get; } = text;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LinkedListNode<Prompt>? Node { get; set; }

        public CancellationTokenRegistration Registration { get; set; }

        public void Complete()
        {
            Completion.TrySetResult();
            Registration.Dispose();
        }

        public void Cancel()
        {
            Completion.TrySetCanceled();
            Registration.Dispose();
        }

        public void Fail(Exception exception)
        {
            Completion.TrySetException(exception);
            Registration.Dispose();
        }
    }
}
