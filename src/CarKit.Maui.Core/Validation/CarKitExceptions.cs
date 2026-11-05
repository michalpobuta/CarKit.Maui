namespace CarKit.Maui;

/// <summary>Base of all CarKit exceptions.</summary>
public class CarKitException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CarKitException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarKitException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarKitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when an operation needs a connected car.</summary>
public sealed class CarNotConnectedException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarNotConnectedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarNotConnectedException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarNotConnectedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when a template is not allowed in any declared app category.</summary>
public sealed class CarTemplateNotAllowedException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarTemplateNotAllowedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarTemplateNotAllowedException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarTemplateNotAllowedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal CarTemplateNotAllowedException(string message, CarTemplateKind kind, CarAppCategory categories)
        : base(message)
    {
        Kind = kind;
        Categories = categories;
    }

    /// <summary>Gets the rejected template kind.</summary>
    public CarTemplateKind? Kind { get; }

    /// <summary>Gets the declared categories.</summary>
    public CarAppCategory Categories { get; }
}

/// <summary>Thrown when the platform does not support a template and no fallback applies.</summary>
public sealed class CarTemplateNotSupportedException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarTemplateNotSupportedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarTemplateNotSupportedException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarTemplateNotSupportedException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal CarTemplateNotSupportedException(string message, CarTemplateKind kind, CarPlatform platform)
        : base(message)
    {
        Kind = kind;
        Platform = platform;
    }

    /// <summary>Gets the unsupported template kind.</summary>
    public CarTemplateKind? Kind { get; }

    /// <summary>Gets the platform.</summary>
    public CarPlatform? Platform { get; }
}

/// <summary>Thrown when a template exceeds an item limit or the stack exceeds its depth.</summary>
public sealed class CarTemplateLimitException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarTemplateLimitException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarTemplateLimitException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarTemplateLimitException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal CarTemplateLimitException(string message, CarLimitKind limit, int maximum, int actual)
        : base(message)
    {
        Limit = limit;
        Maximum = maximum;
        Actual = actual;
    }

    /// <summary>Gets the exceeded limit.</summary>
    public CarLimitKind? Limit { get; }

    /// <summary>Gets the maximum allowed.</summary>
    public int Maximum { get; }

    /// <summary>Gets the actual count.</summary>
    public int Actual { get; }
}

/// <summary>Thrown when a navigation stack operation is invalid, such as pushing the same template twice.</summary>
public sealed class CarStackException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarStackException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarStackException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CarStackException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Wraps an error thrown by the native platform.</summary>
public sealed class CarPlatformException : CarKitException
{
    /// <summary>Creates the exception.</summary>
    public CarPlatformException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public CarPlatformException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The native error.</param>
    public CarPlatformException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
