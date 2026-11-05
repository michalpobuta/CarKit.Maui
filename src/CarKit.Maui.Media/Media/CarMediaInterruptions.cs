namespace CarKit.Maui.Media;

// Shared interruption policy (spec: "Media module decisions (Stage 5)"): iOS AVAudioSession and Android audio focus
// report events here, and the policy pauses and resumes the player through the controller. The resume marker is set
// only by an interruption of ongoing playback; it is cleared by a pause/play command, a permanent focus loss and an
// output disconnect. Main thread only.
internal sealed class CarMediaInterruptions(CarMediaController controller)
{
    public bool ResumePending { get; private set; }

    public Task BeganAsync()
    {
        if (!controller.State.IsActive && !controller.StoppedPlayingRecently)
        {
            return Task.CompletedTask;
        }

        ResumePending = true;
        return controller.PauseCoreAsync();
    }

    public Task EndedAsync(bool shouldResume)
    {
        var resume = ResumePending && shouldResume;
        ResumePending = false;
        return resume ? controller.ResumeCoreAsync() : Task.CompletedTask;
    }

    // Android: a temporary loss (also CAN_DUCK below API 26) like the start of an interruption; a permanent one without
    // resume
    public Task FocusLostAsync(bool transient) => transient ? BeganAsync() : StopAsync();

    public Task FocusGainedAsync() => EndedAsync(shouldResume: true);

    // iOS OldDeviceUnavailable, Android ACTION_AUDIO_BECOMING_NOISY: headphones/car disconnected, pause without resume
    public Task OutputDisconnectedAsync() => StopAsync();

    internal void CancelResume() => ResumePending = false;

    private Task StopAsync()
    {
        ResumePending = false;
        return controller.State.IsActive ? controller.PauseCoreAsync() : Task.CompletedTask;
    }
}
