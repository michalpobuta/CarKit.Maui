using System.Collections.Concurrent;
using AVFoundation;
using CallKit;
using CoreFoundation;
using Foundation;

namespace CarKit.Maui.Communication;

// ICarCallManager on CallKit (spec: "Calls: iOS"): CXProvider reports calls to the system (phone, lock screen, CarPlay
// — the Phone app), CXCallController sends user requests, and system actions go to the app's ICarCallHandler and end
// with Fulfill/Fail. State is kept by CarCallRegistry; the CXProvider delegate is on the main queue. A provider reset
// (callservicesd unavailable, e.g. the simulator) ends calls and pending requests — the CallKit completion does not
// arrive after a reset, so without this the ReportIncomingCallAsync task would hang forever (simulator probe, Stage 6).
internal sealed class CarCallKitManager : ICarCallManager
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<NSError?>> _pending = new();
    private readonly CarCommunicationRuntime _runtime;
    private readonly Lazy<(CXProvider Provider, CXCallController Controller, ProviderDelegate Delegate)> _callKit;

    public CarCallKitManager(CarCommunicationRuntime runtime)
    {
        _runtime = runtime;
        _callKit = new(Create);
        _runtime.Calls.Changed += (_, call) => CallChanged?.Invoke(this, call);
    }

    public event EventHandler<CarCall>? CallChanged;

    public bool IsSupported => true;

    public IReadOnlyList<CarCall> Calls => _runtime.Calls.Calls;

    internal CXProvider Provider => _callKit.Value.Provider;

    public async Task<CarCall> ReportIncomingCallAsync(CarCallHandle caller, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var call = _runtime.Calls.Add(new CarCall(Guid.NewGuid(), caller, CarCallDirection.Incoming) { State = CarCallState.Ringing, DisplayName = displayName });
        var provider = Provider;
        // The system rejected the call (e.g. Focus mode, blocked number) → call ended as Failed
        await SystemAsync(call.Id, "report the incoming call", done => provider.ReportNewIncomingCall(Uuid(call.Id), Update(call), done));
        return _runtime.Calls.Find(call.Id) ?? call;
    }

    public async Task<CarCall> StartOutgoingCallAsync(CarCallHandle callee, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(callee);
        var call = _runtime.Calls.Add(new CarCall(Guid.NewGuid(), callee, CarCallDirection.Outgoing) { State = CarCallState.Dialing, DisplayName = displayName });
        var controller = _callKit.Value.Controller;
        var action = new CXStartCallAction(Uuid(call.Id), Handle(callee)) { ContactIdentifier = displayName };
        await SystemAsync(call.Id, "start the outgoing call", done => controller.RequestTransaction(action, done));
        return _runtime.Calls.Find(call.Id) ?? call;
    }

    public Task EndCallAsync(Guid callId) => RequestAsync(callId, uuid => new CXEndCallAction(uuid));

    public Task SetHeldAsync(Guid callId, bool onHold) => RequestAsync(callId, uuid => new CXSetHeldCallAction(uuid, onHold));

    public Task SetMutedAsync(Guid callId, bool muted) => RequestAsync(callId, uuid => new CXSetMutedCallAction(uuid, muted));

    public void ReportOutgoingCallConnected(Guid callId)
    {
        if (_runtime.Calls.ConnectOutgoing(callId) is not null)
        {
            Provider.ReportConnectedOutgoingCall(Uuid(callId), NSDate.Now);
        }
    }

    public void ReportCallEnded(Guid callId, CarCallEndReason reason)
    {
        var ended = reason switch
        {
            CarCallEndReason.RemoteEnded => CXCallEndedReason.RemoteEnded,
            CarCallEndReason.Failed => CXCallEndedReason.Failed,
            CarCallEndReason.Unanswered => CXCallEndedReason.Unanswered,
            CarCallEndReason.AnsweredElsewhere => CXCallEndedReason.AnsweredElsewhere,
            CarCallEndReason.DeclinedElsewhere => CXCallEndedReason.DeclinedElsewhere,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A call the user ended is ended with EndCallAsync."),
        };
        if (_runtime.Calls.Transition(callId, CarCallState.Ended, reason) is not null)
        {
            Provider.ReportCall(Uuid(callId), NSDate.Now, ended);
        }
    }

    internal static NSUuid Uuid(Guid id) => new(id.ToString());

    private static CXHandle Handle(CarCallHandle handle) => new(
        handle.Type switch
        {
            CarCallHandleType.PhoneNumber => CXHandleType.PhoneNumber,
            CarCallHandleType.EmailAddress => CXHandleType.EmailAddress,
            _ => CXHandleType.Generic,
        },
        handle.Value);

    private static CXCallUpdate Update(CarCall call)
    {
        var update = new CXCallUpdate
        {
            RemoteHandle = Handle(call.Handle),
            SupportsHolding = true,
            SupportsGrouping = false,
            SupportsUngrouping = false,
            SupportsDtmf = false,
            HasVideo = false,
        };
        if (call.DisplayName is { } name)
        {
            update.LocalizedCallerName = name;
        }

        return update;
    }

    private Task RequestAsync(Guid callId, Func<NSUuid, CXCallAction> action)
    {
        if (_runtime.Calls.Find(callId) is null)
        {
            throw new InvalidOperationException($"Call {callId} is not active.");
        }

        var controller = _callKit.Value.Controller;
        var request = action(Uuid(callId));
        return SystemAsync(null, "change the call", done => controller.RequestTransaction(request, done));
    }

    // Request to CallKit with a completion: error → CarPlatformException (new call → Ended/Failed), provider reset
    // before
    // completion → CarPlatformException (OnReset)
    private async Task SystemAsync(Guid? newCall, string what, Action<Action<NSError?>> request)
    {
        var key = Guid.NewGuid();
        var completion = new TaskCompletionSource<NSError?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = completion;
        request(error =>
        {
            _pending.TryRemove(key, out _);
            completion.TrySetResult(error);
        });
        Exception? failure = null;
        try
        {
            if (await completion.Task is { } error)
            {
                failure = new CarPlatformException($"CallKit could not {what}: {error.LocalizedDescription}", new NSErrorException(error));
            }
        }
        catch (CarPlatformException ex)
        {
            failure = ex;
        }

        if (failure is not null)
        {
            if (newCall is { } id)
            {
                _runtime.Calls.Transition(id, CarCallState.Ended, CarCallEndReason.Failed);
            }

            throw failure;
        }
    }

    private void OnReset()
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var completion))
            {
                completion.TrySetException(new CarPlatformException("CallKit reset the call provider before it answered (the call service is unavailable, for example on a simulator)."));
            }
        }
    }

    private (CXProvider, CXCallController, ProviderDelegate) Create()
    {
        // Provider name since iOS 14 = the app name from the package (the constructor with a name is obsolete)
        var configuration = new CXProviderConfiguration
        {
            MaximumCallGroups = 1,
            MaximumCallsPerCallGroup = 1,
            SupportsVideo = false,
            IncludesCallsInRecents = _runtime.Options.IncludeCallsInRecents,
            SupportedHandleTypes = new NSSet<NSNumber>(
                NSNumber.FromInt64((long)CXHandleType.Generic), NSNumber.FromInt64((long)CXHandleType.PhoneNumber), NSNumber.FromInt64((long)CXHandleType.EmailAddress)),
        };
        var provider = new CXProvider(configuration);
        var callDelegate = new ProviderDelegate(_runtime, new CarCallActions(_runtime.Calls, _runtime.CallHandler, _runtime.Errors), OnReset);
        provider.SetDelegate(callDelegate, DispatchQueue.MainQueue);
        return (provider, new CXCallController(DispatchQueue.MainQueue), callDelegate);
    }

    // System actions: the decision (app handler, state transition) is made by the neutral CarCallActions, here only
    // Fulfill/Fail and the CallKit reports. A provider reset also ends calls in the app handler (VoIP engine)
    private sealed class ProviderDelegate(CarCommunicationRuntime runtime, CarCallActions actions, Action reset) : CXProviderDelegate
    {
        public override void DidReset(CXProvider provider)
        {
            CarCommunicationLog.Warning(runtime.Logger, "CallKit reset the call provider; all calls ended", null);
            foreach (var call in runtime.Calls.Calls)
            {
                if (runtime.Calls.Transition(call.Id, CarCallState.Ended, CarCallEndReason.Failed) is { } ended)
                {
                    _ = EndInAppAsync(ended);
                }
            }

            reset();
        }

        public override void PerformAnswerCallAction(CXProvider provider, CXAnswerCallAction action) =>
            Perform(action, CarCallActionKind.Answer);

        public override void PerformStartCallAction(CXProvider provider, CXStartCallAction action) =>
            Perform(action, CarCallActionKind.Start, fulfilled: id =>
            {
                // The call may have already connected in OnStartAsync (ReportOutgoingCallConnected) — then no
                // "connecting"
                if (runtime.Calls.Find(id) is { State: CarCallState.Connecting } call)
                {
                    provider.ReportConnectingOutgoingCall(action.CallUuid, NSDate.Now);
                    if (call.DisplayName is { } name)
                    {
                        provider.ReportCall(action.CallUuid, new CXCallUpdate { LocalizedCallerName = name });
                    }
                }
            });

        public override void PerformEndCallAction(CXProvider provider, CXEndCallAction action) =>
            Perform(action, CarCallActionKind.End);

        public override void PerformSetHeldCallAction(CXProvider provider, CXSetHeldCallAction action) =>
            Perform(action, CarCallActionKind.Hold, action.OnHold);

        public override void PerformSetMutedCallAction(CXProvider provider, CXSetMutedCallAction action) =>
            Perform(action, CarCallActionKind.Mute, action.Muted);

        public override void DidActivateAudioSession(CXProvider provider, AVAudioSession audioSession) =>
            Safe("Communication.AudioSession", runtime.CallHandler.OnAudioSessionActivated);

        public override void DidDeactivateAudioSession(CXProvider provider, AVAudioSession audioSession) =>
            Safe("Communication.AudioSession", runtime.CallHandler.OnAudioSessionDeactivated);

        private void Perform(CXCallAction action, CarCallActionKind kind, bool value = false, Action<Guid>? fulfilled = null) =>
            _ = PerformAsync(action, kind, value, fulfilled);

        private async Task PerformAsync(CXCallAction action, CarCallActionKind kind, bool value, Action<Guid>? fulfilled)
        {
            if (!Guid.TryParse(action.CallUuid.AsString(), out var id) || !await actions.PerformAsync(id, kind, value))
            {
                action.Fail();
                return;
            }

            action.Fulfill();
            fulfilled?.Invoke(id);
        }

        private async Task EndInAppAsync(CarCall call)
        {
            try
            {
                await runtime.CallHandler.OnEndAsync(call);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                runtime.Errors.HandleError(ex, new CarErrorContext("Communication.End", null));
            }
        }

        private void Safe(string operation, Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                runtime.Errors.HandleError(ex, new CarErrorContext(operation, null));
            }
        }
    }
}
