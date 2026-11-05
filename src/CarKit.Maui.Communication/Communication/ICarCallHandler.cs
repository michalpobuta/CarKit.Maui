namespace CarKit.Maui.Communication;

/// <summary>
/// Performs the call actions the system asks for (CarPlay, the lock screen, Siri): connect the app's VoIP engine, end,
/// hold or mute it. Register it with <see cref="CarCommunicationBuilder.UseCallHandler{THandler}"/>. Methods run on the
/// main thread; the default implementations accept every action.
/// </summary>
public interface ICarCallHandler
{
    /// <summary>Answers an incoming call.</summary>
    /// <param name="call">The call.</param>
    /// <returns><see langword="true"/> when the call connected; <see langword="false"/> fails the action.</returns>
    Task<bool> OnAnswerAsync(CarCall call) => Task.FromResult(true);

    /// <summary>Starts an outgoing call.</summary>
    /// <param name="call">The call.</param>
    /// <returns><see langword="true"/> when the call started connecting; <see langword="false"/> fails the action.</returns>
    Task<bool> OnStartAsync(CarCall call) => Task.FromResult(true);

    /// <summary>Ends a call.</summary>
    /// <param name="call">The call.</param>
    /// <returns>A task that completes when the call is ended.</returns>
    Task OnEndAsync(CarCall call) => Task.CompletedTask;

    /// <summary>Puts a call on hold or resumes it.</summary>
    /// <param name="call">The call.</param>
    /// <param name="onHold">Whether the call goes on hold.</param>
    /// <returns><see langword="true"/> when the change succeeded.</returns>
    Task<bool> OnSetHeldAsync(CarCall call, bool onHold) => Task.FromResult(true);

    /// <summary>Mutes or unmutes the microphone.</summary>
    /// <param name="call">The call.</param>
    /// <param name="muted">Whether the microphone is muted.</param>
    /// <returns><see langword="true"/> when the change succeeded.</returns>
    Task<bool> OnSetMutedAsync(CarCall call, bool muted) => Task.FromResult(true);

    /// <summary>Called when the system activated the audio session for calls; start call audio here.</summary>
    void OnAudioSessionActivated()
    {
    }

    /// <summary>Called when the system deactivated the call audio session.</summary>
    void OnAudioSessionDeactivated()
    {
    }
}
