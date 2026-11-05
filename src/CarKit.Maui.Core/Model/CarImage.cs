namespace CarKit.Maui;

/// <summary>An image shown in the car. Create it with the static factory methods.</summary>
public abstract record CarImage
{
    private protected CarImage()
    {
    }

    /// <summary>Gets the variant used in dark mode, if different.</summary>
    public CarImage? Dark { get; init; }

    /// <summary>Gets the tint applied by platforms that support it (Android Auto <c>CarIcon</c>).</summary>
    public CarColor? Tint { get; init; }

    /// <summary>Returns a copy with a dark-mode variant.</summary>
    /// <param name="dark">The dark-mode image.</param>
    /// <returns>The new image.</returns>
    public CarImage WithDarkVariant(CarImage dark)
    {
        ArgumentNullException.ThrowIfNull(dark);
        return this with { Dark = dark };
    }

    /// <summary>Creates an image from an app file or bundled resource.</summary>
    /// <param name="path">The file name or path.</param>
    /// <returns>The image.</returns>
    public static CarImage FromFile(string path) => new CarFileImage(path);

    /// <summary>Creates an image downloaded from a URI.</summary>
    /// <param name="uri">The image URI.</param>
    /// <returns>The image.</returns>
    public static CarImage FromUri(Uri uri) => new CarUriImage(uri);

    /// <summary>Creates an image from encoded bytes (PNG, JPEG).</summary>
    /// <param name="data">The encoded image.</param>
    /// <returns>The image.</returns>
    public static CarImage FromBytes(ReadOnlySpan<byte> data) => new CarBytesImage(data);

    /// <summary>Creates an image read from a stream opened on demand.</summary>
    /// <param name="open">
    /// Opens the stream; the caller disposes it. CarKit may call it more than once, for example once per image size when the
    /// same image is shown in a list row and a grid button, so return a new stream on every call.
    /// </param>
    /// <returns>The image.</returns>
    public static CarImage FromStream(Func<CancellationToken, Task<Stream>> open) => new CarStreamImage(open);

    /// <summary>Creates a system symbol image.</summary>
    /// <param name="iosSymbolName">The SF Symbol name used on CarPlay.</param>
    /// <param name="androidIconName">The Material icon name used on Android Auto.</param>
    /// <returns>The image.</returns>
    public static CarImage FromSystemSymbol(string? iosSymbolName, string? androidIconName) =>
        new CarSymbolImage(iosSymbolName, androidIconName);
}

/// <summary>An image from an app file or bundled resource.</summary>
public sealed record CarFileImage : CarImage
{
    /// <summary>Creates the image.</summary>
    /// <param name="path">The file name or path.</param>
    public CarFileImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
    }

    /// <summary>Gets the file name or path.</summary>
    public string Path { get; }
}

/// <summary>An image downloaded from a URI.</summary>
public sealed record CarUriImage : CarImage
{
    /// <summary>Creates the image.</summary>
    /// <param name="uri">The image URI.</param>
    public CarUriImage(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        Uri = uri;
    }

    /// <summary>Gets the image URI.</summary>
    public Uri Uri { get; }
}

/// <summary>An image from encoded bytes.</summary>
public sealed record CarBytesImage : CarImage
{
    /// <summary>Creates the image.</summary>
    /// <param name="data">The encoded image.</param>
    public CarBytesImage(ReadOnlySpan<byte> data) => Data = CarArray.Create(data);

    /// <summary>Gets the encoded image.</summary>
    public CarArray<byte> Data { get; }
}

/// <summary>An image read from a stream opened on demand.</summary>
public sealed record CarStreamImage : CarImage
{
    private readonly Func<CancellationToken, Task<Stream>> _open;

    /// <summary>Creates the image.</summary>
    /// <param name="open">
    /// Opens the stream; the caller disposes it. It may be called more than once (once per image size), so it returns a new
    /// stream on every call.
    /// </param>
    public CarStreamImage(Func<CancellationToken, Task<Stream>> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    /// <summary>Opens the image stream.</summary>
    /// <param name="cancellationToken">Cancels opening.</param>
    /// <returns>The stream; the caller disposes it.</returns>
    public Task<Stream> OpenAsync(CancellationToken cancellationToken) => _open(cancellationToken);
}

/// <summary>A system symbol: SF Symbol on CarPlay, Material icon on Android Auto.</summary>
public sealed record CarSymbolImage : CarImage
{
    /// <summary>Creates the image; at least one name is required.</summary>
    /// <param name="iosSymbolName">The SF Symbol name.</param>
    /// <param name="androidIconName">The Material icon name.</param>
    public CarSymbolImage(string? iosSymbolName, string? androidIconName)
    {
        if (string.IsNullOrWhiteSpace(iosSymbolName) && string.IsNullOrWhiteSpace(androidIconName))
        {
            throw new ArgumentException("At least one symbol name is required.", nameof(iosSymbolName));
        }

        IosSymbolName = iosSymbolName;
        AndroidIconName = androidIconName;
    }

    /// <summary>Gets the SF Symbol name.</summary>
    public string? IosSymbolName { get; }

    /// <summary>Gets the Material icon name.</summary>
    public string? AndroidIconName { get; }
}
