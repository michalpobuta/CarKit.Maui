namespace CarKit.Maui.Communication;

/// <summary>The kind of a <see cref="CarCallHandle"/>.</summary>
public enum CarCallHandleType
{
    /// <summary>An app-specific identifier, such as a user name.</summary>
    Generic,

    /// <summary>A phone number.</summary>
    PhoneNumber,

    /// <summary>An e-mail address.</summary>
    EmailAddress,
}

/// <summary>Who is called or calling.</summary>
public sealed record CarCallHandle
{
    /// <summary>Creates the handle.</summary>
    /// <param name="type">The handle kind.</param>
    /// <param name="value">The phone number, e-mail address or app identifier.</param>
    public CarCallHandle(CarCallHandleType type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Type = type;
        Value = value;
    }

    /// <summary>Gets the handle kind.</summary>
    public CarCallHandleType Type { get; }

    /// <summary>Gets the value.</summary>
    public string Value { get; }
}

/// <summary>The direction of a <see cref="CarCall"/>.</summary>
public enum CarCallDirection
{
    /// <summary>The other person called.</summary>
    Incoming,

    /// <summary>The user called.</summary>
    Outgoing,
}

/// <summary>The state of a <see cref="CarCall"/>.</summary>
public enum CarCallState
{
    /// <summary>An incoming call waits to be answered.</summary>
    Ringing,

    /// <summary>An outgoing call was requested.</summary>
    Dialing,

    /// <summary>An outgoing call is connecting.</summary>
    Connecting,

    /// <summary>The call is connected.</summary>
    Active,

    /// <summary>The call is on hold.</summary>
    Held,

    /// <summary>The call ended.</summary>
    Ended,
}

/// <summary>Why a <see cref="CarCall"/> ended.</summary>
public enum CarCallEndReason
{
    /// <summary>The user ended the call (in the app, the car or the system call UI).</summary>
    Local,

    /// <summary>The other person ended the call.</summary>
    RemoteEnded,

    /// <summary>The call failed.</summary>
    Failed,

    /// <summary>Nobody answered.</summary>
    Unanswered,

    /// <summary>The call was answered on another device.</summary>
    AnsweredElsewhere,

    /// <summary>The call was declined on another device.</summary>
    DeclinedElsewhere,
}

/// <summary>A VoIP call the system (CallKit) shows on the phone and in the car.</summary>
/// <param name="Id">The call identifier (CallKit UUID).</param>
/// <param name="Handle">Who is called or calling.</param>
/// <param name="Direction">The direction.</param>
public sealed record CarCall(Guid Id, CarCallHandle Handle, CarCallDirection Direction)
{
    /// <summary>Gets the name shown for the other person.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the state.</summary>
    public CarCallState State { get; init; }

    /// <summary>Gets a value indicating whether the microphone is muted.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Gets why the call ended; <see langword="null"/> until <see cref="CarCallState.Ended"/>.</summary>
    public CarCallEndReason? EndReason { get; init; }
}
