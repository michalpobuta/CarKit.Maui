namespace CarKit.Maui;

/// <summary>Base of the sign-in methods of <see cref="AndroidAutoSignInTemplate"/>.</summary>
public abstract record AndroidAutoSignInMethod
{
    private protected AndroidAutoSignInMethod()
    {
    }
}

/// <summary>Shows a PIN the user enters on the phone or website.</summary>
public sealed record AndroidAutoPinSignIn : AndroidAutoSignInMethod
{
    /// <summary>Creates the method.</summary>
    /// <param name="pin">The PIN, 1 to 12 characters.</param>
    public AndroidAutoPinSignIn(string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        if (pin.Length > 12)
        {
            throw new ArgumentException("The PIN can have at most 12 characters.", nameof(pin));
        }

        Pin = pin;
    }

    /// <summary>Gets the PIN.</summary>
    public string Pin { get; }
}

/// <summary>Shows a QR code the user scans with the phone.</summary>
public sealed record AndroidAutoQrCodeSignIn : AndroidAutoSignInMethod
{
    /// <summary>Creates the method.</summary>
    /// <param name="uri">The URI encoded in the QR code.</param>
    public AndroidAutoQrCodeSignIn(Uri uri) => Uri = uri ?? throw new ArgumentNullException(nameof(uri));

    /// <summary>Gets the URI encoded in the QR code.</summary>
    public Uri Uri { get; }
}

/// <summary>Kind of text entered in <see cref="AndroidAutoInputSignIn"/>.</summary>
public enum AndroidAutoInputType
{
    /// <summary>Plain text.</summary>
    Default,

    /// <summary>A password; characters are hidden.</summary>
    Password,
}

/// <summary>Keyboard shown for <see cref="AndroidAutoInputSignIn"/>.</summary>
public enum AndroidAutoKeyboardType
{
    /// <summary>The default keyboard.</summary>
    Default,

    /// <summary>An e-mail keyboard.</summary>
    Email,

    /// <summary>A phone keyboard.</summary>
    Phone,

    /// <summary>A numeric keyboard.</summary>
    Number,
}

/// <summary>Asks for a user name or password on the car screen.</summary>
public sealed record AndroidAutoInputSignIn : AndroidAutoSignInMethod
{
    /// <summary>Gets the hint shown in the empty field.</summary>
    public CarText? Hint { get; init; }

    /// <summary>Gets the initial value.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Gets the input type.</summary>
    public AndroidAutoInputType InputType { get; init; }

    /// <summary>Gets the keyboard type.</summary>
    public AndroidAutoKeyboardType KeyboardType { get; init; }

    /// <summary>Gets the error shown under the field.</summary>
    public CarText? ErrorMessage { get; init; }

    /// <summary>Gets the handler called when the user submits the text.</summary>
    public Func<CarTextInputContext, Task>? OnInputSubmitted { get; init; }
}

/// <summary>Signs in through an identity provider such as Google Sign-In.</summary>
public sealed record AndroidAutoProviderSignIn : AndroidAutoSignInMethod
{
    /// <summary>Creates the method.</summary>
    /// <param name="action">The provider button.</param>
    public AndroidAutoProviderSignIn(CarAction action) => Action = action ?? throw new ArgumentNullException(nameof(action));

    /// <summary>Gets the provider button.</summary>
    public CarAction Action { get; }
}
