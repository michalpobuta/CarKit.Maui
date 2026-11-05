namespace CarKit.Maui.Communication;

/// <summary>
/// Reports VoIP calls to the system so they appear on the phone and in the car (iOS: CallKit, shown by the CarPlay Phone app).
/// On Android calls need Jetpack Core-Telecom, which has no .NET binding yet: <see cref="IsSupported"/> is
/// <see langword="false"/> and the methods throw <see cref="PlatformNotSupportedException"/>. System actions (answer, end,
/// hold, mute from the car or the lock screen) arrive at <see cref="ICarCallHandler"/>.
/// </summary>
public interface ICarCallManager
{
    /// <summary>Gets a value indicating whether the platform supports calls.</summary>
    bool IsSupported { get; }

    /// <summary>Gets the calls that have not ended.</summary>
    IReadOnlyList<CarCall> Calls { get; }

    /// <summary>Occurs on the main thread when a call starts, changes or ends.</summary>
    event EventHandler<CarCall>? CallChanged;

    /// <summary>Reports an incoming call; the system rings and shows it on the phone and in the car.</summary>
    /// <param name="caller">Who is calling.</param>
    /// <param name="displayName">The name shown for the caller.</param>
    /// <returns>The ringing call.</returns>
    /// <exception cref="CarPlatformException">The system rejected the call (for example Focus or a blocked number) or reset the call service; the call ended as <see cref="CarCallEndReason.Failed"/>.</exception>
    Task<CarCall> ReportIncomingCallAsync(CarCallHandle caller, string? displayName = null);

    /// <summary>
    /// Starts an outgoing call; the system then calls <see cref="ICarCallHandler.OnStartAsync"/>. Report the connection
    /// with <see cref="ReportOutgoingCallConnected"/>.
    /// </summary>
    /// <param name="callee">Who is called.</param>
    /// <param name="displayName">The name shown for the callee.</param>
    /// <returns>The call.</returns>
    /// <exception cref="CarPlatformException">The system rejected the request or reset the call service; the call ended as <see cref="CarCallEndReason.Failed"/>.</exception>
    Task<CarCall> StartOutgoingCallAsync(CarCallHandle callee, string? displayName = null);

    /// <summary>Ends a call (the system then calls <see cref="ICarCallHandler.OnEndAsync"/>).</summary>
    /// <param name="callId">The call identifier.</param>
    /// <returns>A task that completes when the system accepted the request.</returns>
    /// <exception cref="InvalidOperationException">The call is unknown or already ended.</exception>
    Task EndCallAsync(Guid callId);

    /// <summary>Puts a call on hold or resumes it.</summary>
    /// <param name="callId">The call identifier.</param>
    /// <param name="onHold">Whether the call is on hold.</param>
    /// <returns>A task that completes when the system accepted the request.</returns>
    Task SetHeldAsync(Guid callId, bool onHold);

    /// <summary>Mutes or unmutes the microphone of a call.</summary>
    /// <param name="callId">The call identifier.</param>
    /// <param name="muted">Whether the microphone is muted.</param>
    /// <returns>A task that completes when the system accepted the request.</returns>
    Task SetMutedAsync(Guid callId, bool muted);

    /// <summary>
    /// Reports that an outgoing call connected; it may be called inside <see cref="ICarCallHandler.OnStartAsync"/> or later.
    /// Does nothing for a call that already ended.
    /// </summary>
    /// <param name="callId">The call identifier.</param>
    /// <exception cref="InvalidOperationException">The call is unknown, incoming, or not dialing or connecting.</exception>
    void ReportOutgoingCallConnected(Guid callId);

    /// <summary>Reports a call that ended without a request from the user, for example when the other person hung up.</summary>
    /// <param name="callId">The call identifier.</param>
    /// <param name="reason">Why the call ended; not <see cref="CarCallEndReason.Local"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is <see cref="CarCallEndReason.Local"/>; end such calls with <see cref="EndCallAsync"/>.</exception>
    void ReportCallEnded(Guid callId, CarCallEndReason reason);
}
