namespace CarKit.Maui.Communication;

// A system action for a call (CallKit: CXAnswer/Start/End/SetHeld/SetMuted)
internal enum CarCallActionKind
{
    Answer,
    Start,
    End,
    Hold,
    Mute,
}

// The decision for a system action, shared and tested on net10.0: the app handler, then the state transition;
// true = Fulfill, false = Fail (unknown call, refusal or handler exception: a new call then ends as Failed)
internal sealed class CarCallActions(CarCallRegistry calls, ICarCallHandler handler, ICarErrorHandler errors)
{
    public async Task<bool> PerformAsync(Guid id, CarCallActionKind kind, bool value = false)
    {
        if (calls.Find(id) is not { } call)
        {
            return false;
        }

        try
        {
            var accepted = kind switch
            {
                CarCallActionKind.Answer => await handler.OnAnswerAsync(call),
                CarCallActionKind.Start => await handler.OnStartAsync(call),
                CarCallActionKind.End => await End(call),
                CarCallActionKind.Hold => await handler.OnSetHeldAsync(call, value),
                _ => await handler.OnSetMutedAsync(call, value),
            };
            if (accepted)
            {
                switch (kind)
                {
                    case CarCallActionKind.Answer:
                        calls.Transition(id, CarCallState.Active);
                        break;
                    case CarCallActionKind.Start:
                        // The app may have already reported the connection in OnStartAsync (review I2) or ended the call
                        if (calls.Find(id) is not { } started)
                        {
                            return false;
                        }

                        if (started.State == CarCallState.Dialing)
                        {
                            calls.Transition(id, CarCallState.Connecting);
                        }

                        break;
                    case CarCallActionKind.End:
                        calls.Transition(id, CarCallState.Ended, CarCallEndReason.Local);
                        break;
                    case CarCallActionKind.Hold:
                        calls.Transition(id, value ? CarCallState.Held : CarCallState.Active);
                        break;
                    default:
                        calls.SetMuted(id, value);
                        break;
                }

                return true;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.HandleError(ex, new CarErrorContext("Communication." + kind, null));
        }

        if (kind is CarCallActionKind.Start or CarCallActionKind.Answer)
        {
            calls.Transition(id, CarCallState.Ended, CarCallEndReason.Failed);
        }

        return false;
    }

    private async Task<bool> End(CarCall call)
    {
        await handler.OnEndAsync(call);
        return true;
    }
}
