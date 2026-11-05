namespace CarKit.Maui.Communication;

// Call state (spec: "Communication module decisions (Stage 6)"): Ringing → Active, Dialing → Connecting → Active (or
// Dialing → Active), Active ↔ Held, any → Ended. Other transitions → InvalidOperationException; ending again is a no-op
// (CallKit and the app may both report the end). Changes: a list without ended calls and an event on the main thread.
internal sealed class CarCallRegistry(ICarDispatcher dispatcher)
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, CarCall> _calls = [];
    private readonly HashSet<Guid> _ended = [];

    public event EventHandler<CarCall>? Changed;

    public IReadOnlyList<CarCall> Calls
    {
        get
        {
            lock (_sync)
            {
                return [.. _calls.Values];
            }
        }
    }

    public CarCall? Find(Guid id)
    {
        lock (_sync)
        {
            return _calls.GetValueOrDefault(id);
        }
    }

    public CarCall Add(CarCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.State is not (CarCallState.Ringing or CarCallState.Dialing))
        {
            throw new ArgumentException($"A new call starts Ringing or Dialing, not {call.State}.", nameof(call));
        }

        lock (_sync)
        {
            if (_ended.Contains(call.Id) || !_calls.TryAdd(call.Id, call))
            {
                throw new InvalidOperationException($"Call {call.Id} already exists.");
            }
        }

        Raise(call);
        return call;
    }

    // null = call already ended (no-op); unknown call → InvalidOperationException
    public CarCall? Transition(Guid id, CarCallState state, CarCallEndReason? reason = null)
    {
        CarCall updated;
        lock (_sync)
        {
            if (!_calls.TryGetValue(id, out var call))
            {
                return _ended.Contains(id) ? null : throw new InvalidOperationException($"Unknown call {id}.");
            }

            if (call.State == state && state != CarCallState.Ended)
            {
                return call;
            }

            if (!Allowed(call.State, state))
            {
                throw new InvalidOperationException($"Call {id} cannot go from {call.State} to {state}.");
            }

            updated = call with { State = state, EndReason = state == CarCallState.Ended ? reason ?? CarCallEndReason.Local : null };
            if (state == CarCallState.Ended)
            {
                _calls.Remove(id);
                _ended.Add(id);
            }
            else
            {
                _calls[id] = updated;
            }
        }

        Raise(updated);
        return updated;
    }

    // Connection of an outgoing call reported by the app (ReportOutgoingCallConnected): only an outgoing call in
    // Dialing or Connecting (review I1: an incoming or held call cannot "connect" without a system action);
    // null = call already ended
    public CarCall? ConnectOutgoing(Guid id)
    {
        lock (_sync)
        {
            if (_calls.TryGetValue(id, out var call)
                && (call.Direction != CarCallDirection.Outgoing || call.State is not (CarCallState.Dialing or CarCallState.Connecting)))
            {
                throw new InvalidOperationException($"Call {id} is a {call.Direction} call in state {call.State}; only an outgoing call that is dialing or connecting can be reported connected.");
            }
        }

        return Transition(id, CarCallState.Active);
    }

    public CarCall? SetMuted(Guid id, bool muted)
    {
        CarCall updated;
        lock (_sync)
        {
            if (!_calls.TryGetValue(id, out var call))
            {
                return _ended.Contains(id) ? null : throw new InvalidOperationException($"Unknown call {id}.");
            }

            if (call.IsMuted == muted)
            {
                return call;
            }

            updated = call with { IsMuted = muted };
            _calls[id] = updated;
        }

        Raise(updated);
        return updated;
    }

    private static bool Allowed(CarCallState from, CarCallState to) => (from, to) switch
    {
        (_, CarCallState.Ended) => true,
        (CarCallState.Ringing, CarCallState.Active) => true,
        (CarCallState.Dialing, CarCallState.Connecting or CarCallState.Active) => true,
        (CarCallState.Connecting, CarCallState.Active) => true,
        (CarCallState.Active, CarCallState.Held) => true,
        (CarCallState.Held, CarCallState.Active) => true,
        _ => false,
    };

    private void Raise(CarCall call)
    {
        if (Changed is null)
        {
            return;
        }

        if (dispatcher.IsDispatchRequired)
        {
            _ = dispatcher.DispatchAsync(() =>
            {
                Changed?.Invoke(this, call);
                return Task.CompletedTask;
            });
        }
        else
        {
            Changed.Invoke(this, call);
        }
    }
}

// Platform without calls: Android until the Jetpack Core-Telecom binding exists (spec: Android calls gate)
internal sealed class CarUnsupportedCallManager : ICarCallManager
{
    public const string Message =
        "Calls in Android Auto need Jetpack Core-Telecom, which has no .NET binding yet; CarKit does not support calls on Android (CarKitAndroidCalling, CARKIT004).";

    public static readonly CarUnsupportedCallManager Instance = new();

    public bool IsSupported => false;

    public IReadOnlyList<CarCall> Calls => [];

    public event EventHandler<CarCall>? CallChanged
    {
        add { }
        remove { }
    }

    public Task<CarCall> ReportIncomingCallAsync(CarCallHandle caller, string? displayName = null) => throw new PlatformNotSupportedException(Message);

    public Task<CarCall> StartOutgoingCallAsync(CarCallHandle callee, string? displayName = null) => throw new PlatformNotSupportedException(Message);

    public Task EndCallAsync(Guid callId) => throw new PlatformNotSupportedException(Message);

    public Task SetHeldAsync(Guid callId, bool onHold) => throw new PlatformNotSupportedException(Message);

    public Task SetMutedAsync(Guid callId, bool muted) => throw new PlatformNotSupportedException(Message);

    public void ReportOutgoingCallConnected(Guid callId) => throw new PlatformNotSupportedException(Message);

    public void ReportCallEnded(Guid callId, CarCallEndReason reason) => throw new PlatformNotSupportedException(Message);
}
