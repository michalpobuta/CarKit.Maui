using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Renderers;

// Decoding an image to a native type (UIImage, CarIcon). FromFile and FromSymbol are called on the calling thread
// (main), FromBytes also from the thread pool (URI and stream images). maxPixels: the longer side after scaling.
internal interface ICarImagePlatform<TImage> where TImage : class
{
    TImage? FromFile(string path, int maxPixels);

    TImage? FromSymbol(CarSymbolImage symbol, int maxPixels);

    TImage? FromBytes(byte[] data, int maxPixels);

    TImage Tint(TImage image, CarColor tint);

    // iOS: UIImageAsset with both variants, the choice for the current theme
    TImage Combine(TImage light, TImage dark, CarContentStyle style);
}

internal readonly record struct CarImageRequest(int MaxPixels, CarContentStyle Style);

// Image: the image is available at once; Pending: the image is loading asynchronously (the task never fails, null = no
// image)
internal readonly record struct CarImageLoad<TImage>(TImage? Image, Task<TImage?>? Pending) where TImage : class;

// Image pipeline (spec: "Template decisions (Stage 3)"): file, symbol and bytes at once, URI and stream off the
// calling thread with a 5 MB and 20 s limit, an LRU cache, one download per source; failure → no image + log, never
// an exception. Failed results are cached too: a repeated screen rebuild does not retry the download in a loop.
internal sealed class CarImageLoader<TImage> : IDisposable where TImage : class
{
    public const long MaxBytes = 5L * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly ICarImagePlatform<TImage> _platform;
    private readonly ILogger _logger;
    private readonly bool _combineDarkVariants;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _disposed = new();
    private readonly Lock _lock = new();
    private readonly CarImageCache<ImageKey, Entry> _cache;
    private readonly Dictionary<ImageKey, Task<TImage?>> _pending = [];

    public CarImageLoader(ICarImagePlatform<TImage> platform, ILogger logger, bool combineDarkVariants, HttpMessageHandler? http = null, int capacity = 64)
    {
        _platform = platform;
        _logger = logger;
        _combineDarkVariants = combineDarkVariants;
        _cache = new CarImageCache<ImageKey, Entry>(capacity);
        _http = http is null ? new HttpClient() : new HttpClient(http, disposeHandler: false);
        _http.Timeout = Timeout;
    }

    public CarImageLoad<TImage> Load(CarImage? image, CarImageRequest request)
    {
        if (image is null || _disposed.IsCancellationRequested)
        {
            return default;
        }

        var light = image with { Dark = null };
        if (image.Dark is not { } dark)
        {
            return LoadVariant(light, request.MaxPixels);
        }

        var darkVariant = dark with { Dark = null, Tint = dark.Tint ?? image.Tint };
        if (!_combineDarkVariants)
        {
            return LoadVariant(request.Style == CarContentStyle.Dark ? darkVariant : light, request.MaxPixels);
        }

        var lightLoad = LoadVariant(light, request.MaxPixels);
        var darkLoad = LoadVariant(darkVariant, request.MaxPixels);
        return lightLoad.Pending is null && darkLoad.Pending is null
            ? new(Combine(lightLoad.Image, darkLoad.Image, request.Style), null)
            : new(null, CombineLaterAsync(lightLoad, darkLoad, request.Style));
    }

    public void Dispose()
    {
        if (_disposed.IsCancellationRequested)
        {
            return;
        }

        _disposed.Cancel();
        _http.Dispose();
        _disposed.Dispose();
    }

    private CarImageLoad<TImage> LoadVariant(CarImage source, int maxPixels)
    {
        var key = new ImageKey(source, maxPixels);
        lock (_lock)
        {
            if (_cache.TryGet(key, out var entry))
            {
                return new(entry.Image, null);
            }

            if (_pending.TryGetValue(key, out var pending))
            {
                return new(null, pending);
            }
        }

        return source switch
        {
            CarFileImage file => new(Decode(key, () => _platform.FromFile(file.Path, maxPixels)), null),
            CarSymbolImage symbol => new(Decode(key, () => _platform.FromSymbol(symbol, maxPixels)), null),
            CarBytesImage bytes => new(Decode(key, () => _platform.FromBytes([.. bytes.Data], maxPixels)), null),
            CarUriImage uri when uri.Uri.IsAbsoluteUri && uri.Uri.Scheme is "http" or "https" or "file" =>
                new(null, LoadAsync(key, token => ReadUriAsync(uri.Uri, token))),
            CarStreamImage stream => new(null, LoadAsync(key, token => ReadStreamAsync(stream, token))),
            _ => new(Fail(key, "unsupported image source or URI scheme", null), null),
        };
    }

    private TImage? Decode(ImageKey key, Func<TImage?> decode)
    {
        TImage? image;
        try
        {
            image = Finish(key, decode());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail(key, "decoding failed", ex);
        }

        lock (_lock)
        {
            _cache.Set(key, new Entry(image));
        }

        return image;
    }

    private Task<TImage?> LoadAsync(ImageKey key, Func<CancellationToken, Task<byte[]>> read)
    {
        var token = _disposed.Token;
        var result = new TaskCompletionSource<TImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _pending[key] = result.Task;
        }

        _ = Task.Run(async () =>
        {
            TImage? image = null;
            var completed = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(Timeout);
                var data = await read(timeout.Token).ConfigureAwait(false);
                image = Finish(key, _platform.FromBytes(data, key.MaxPixels));
                completed = true;
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Disconnect (Dispose): no image and no log
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Fail(key, "loading failed", ex);
                completed = true;
            }
            finally
            {
                lock (_lock)
                {
                    _pending.Remove(key);
                    if (completed)
                    {
                        _cache.Set(key, new Entry(image));
                    }
                }

                result.TrySetResult(image);
            }
        }, CancellationToken.None);
        return result.Task;
    }

    private TImage? Finish(ImageKey key, TImage? image)
    {
        if (image is null)
        {
            Fail(key, "the platform could not decode the image", null);
            return null;
        }

        return key.Source.Tint is { } tint ? _platform.Tint(image, tint) : image;
    }

    private TImage? Fail(ImageKey key, string reason, Exception? exception)
    {
        CarKitRenderLog.ImageFailed(_logger, Describe(key.Source), reason, exception);
        lock (_lock)
        {
            _cache.Set(key, new Entry(null));
        }

        return null;
    }

    private async Task<TImage?> CombineLaterAsync(CarImageLoad<TImage> light, CarImageLoad<TImage> dark, CarContentStyle style)
    {
        var lightImage = light.Pending is { } lightPending ? await lightPending.ConfigureAwait(false) : light.Image;
        var darkImage = dark.Pending is { } darkPending ? await darkPending.ConfigureAwait(false) : dark.Image;
        try
        {
            return Combine(lightImage, darkImage, style);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CarKitRenderLog.ImageFailed(_logger, "dark variant", "combining failed", ex);
            return lightImage;
        }
    }

    private TImage? Combine(TImage? light, TImage? dark, CarContentStyle style) =>
        light is not null && dark is not null ? _platform.Combine(light, dark, style) : light ?? dark;

    private async Task<byte[]> ReadUriAsync(Uri uri, CancellationToken token)
    {
        if (uri.IsFile)
        {
            var file = File.OpenRead(uri.LocalPath);
            await using (file.ConfigureAwait(false))
            {
                return await ReadBoundedAsync(file, token).ConfigureAwait(false);
            }
        }

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException($"The image is larger than {MaxBytes} bytes.");
        }

        var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadStreamAsync(CarStreamImage image, CancellationToken token)
    {
        var stream = await image.OpenAsync(token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new InvalidDataException($"The image is larger than {MaxBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Describe(CarImage image) => image switch
    {
        CarFileImage file => file.Path,
        CarUriImage uri => uri.Uri.ToString(),
        CarBytesImage bytes => $"{bytes.Data.Count} bytes",
        CarSymbolImage symbol => $"symbol {symbol.IosSymbolName ?? symbol.AndroidIconName}",
        _ => "stream",
    };

    private readonly record struct ImageKey(CarImage Source, int MaxPixels);

    private sealed record Entry(TImage? Image);
}
