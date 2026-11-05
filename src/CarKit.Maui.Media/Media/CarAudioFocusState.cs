namespace CarKit.Maui.Media;

internal enum CarAudioFocusAction
{
    None,
    Request,
    Abandon,
}

// Android audio focus state without AudioManager (spec: "Media module decisions (Stage 5)"): request on
// Playing/Buffering, release on Stopped/Ended/Idle/Error. Requested = request granted and on the focus stack (abandon),
// Lost = temporarily handed to another app (return via AUDIOFOCUS_GAIN). A denial leaves no request (review I2).
// Main thread only.
internal sealed class CarAudioFocusState
{
    public bool Requested { get; private set; }

    public bool Lost { get; private set; }

    public bool IsHeld => Requested && !Lost;

    public CarAudioFocusAction OnPlaybackState(CarPlaybackState state, bool resumePending)
    {
        if (state.IsActive)
        {
            // Playing despite a temporary loss (the driver pressed play during a call) requests focus anew
            return !Requested || (Lost && !resumePending) ? CarAudioFocusAction.Request : CarAudioFocusAction.None;
        }

        if (Requested && state.Status is CarPlaybackStatus.Stopped or CarPlaybackStatus.Ended or CarPlaybackStatus.Idle or CarPlaybackStatus.Error)
        {
            // The caller releases focus (AbandonAudioFocusRequest): the state is immediately without a request
            OnAbandoned();
            return CarAudioFocusAction.Abandon;
        }

        return CarAudioFocusAction.None;
    }

    public void OnRequestResult(bool granted)
    {
        Requested = granted;
        Lost = false;
    }

    public void OnGain() => Lost = false;

    public void OnTransientLoss() => Lost = true;

    public void OnLoss() => OnAbandoned();

    public void OnAbandoned()
    {
        Requested = false;
        Lost = false;
    }
}
