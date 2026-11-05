using System.Net;
using System.Text;
using CarKit.Maui.Renderers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

// Test platform: an image is a description of its source, so assertions see what was decoded and how
internal sealed class StringImagePlatform : ICarImagePlatform<string>
{
    public ManualResetEventSlim? DecodeGate { get; set; }

    public bool DecodedAfterGate { get; private set; }

    public int Decodes { get; private set; }

    public string? FromFile(string path, int maxPixels) => path.Contains("missing", StringComparison.Ordinal) ? null : $"file:{path}@{maxPixels}";

    public string? FromSymbol(CarSymbolImage symbol, int maxPixels) => $"symbol:{symbol.IosSymbolName}/{symbol.AndroidIconName}@{maxPixels}";

    public string? FromBytes(byte[] data, int maxPixels)
    {
        if (DecodeGate is { } gate)
        {
            DecodedAfterGate = gate.Wait(TimeSpan.FromSeconds(5));
        }

        Decodes++;
        var text = Encoding.UTF8.GetString(data);
        return text.StartsWith("bad", StringComparison.Ordinal) ? null : $"bytes:{text}@{maxPixels}";
    }

    public string Tint(string image, CarColor tint) => $"{image}+tint:{tint.Red:X2}{tint.Green:X2}{tint.Blue:X2}";

    public string Combine(string light, string dark, CarContentStyle style) => $"asset({light}|{dark})@{style}";
}

internal sealed class FakeHttp : HttpMessageHandler
{
    public int Requests { get; private set; }

    public TaskCompletionSource? Release { get; set; }

    public Func<Uri, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("png") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Release is { } release)
        {
            await release.Task.WaitAsync(cancellationToken);
        }

        return Respond(request.RequestUri!);
    }
}

public sealed class ImageLoaderTests : IDisposable
{
    private static readonly CarImageRequest _light = new(64, CarContentStyle.Light);
    private readonly StringImagePlatform _platform = new();
    private readonly FakeHttp _http = new();
    private readonly TestLogger _logger = new();
    private readonly CarImageLoader<string> _loader;

    public ImageLoaderTests() => _loader = new CarImageLoader<string>(_platform, _logger, combineDarkVariants: false, _http);

    public void Dispose()
    {
        _loader.Dispose();
        _http.Dispose();
    }

    [Fact]
    public void File_bytes_and_symbol_load_synchronously()
    {
        Assert.Equal("file:station.png@64", _loader.Load(CarImage.FromFile("station.png"), _light).Image);
        Assert.Equal("bytes:abc@64", _loader.Load(CarImage.FromBytes("abc"u8), _light).Image);
        Assert.Equal("symbol:car.fill/ic_car@64", _loader.Load(CarImage.FromSystemSymbol("car.fill", "ic_car"), _light).Image);
        Assert.Null(_loader.Load(CarImage.FromFile("station.png"), _light).Pending);
        Assert.Equal(default, _loader.Load(null, _light));
    }

    [Fact]
    public void Same_request_hits_the_cache()
    {
        _loader.Load(CarImage.FromBytes("abc"u8), _light);
        _loader.Load(CarImage.FromBytes("abc"u8), _light);
        _loader.Load(CarImage.FromBytes("abc"u8), _light with { MaxPixels = 128 });

        Assert.Equal(2, _platform.Decodes);
    }

    [Fact]
    public void Cache_evicts_least_recently_used()
    {
        using var loader = new CarImageLoader<string>(_platform, _logger, combineDarkVariants: false, _http, capacity: 2);
        loader.Load(CarImage.FromBytes("a"u8), _light);
        loader.Load(CarImage.FromBytes("b"u8), _light);
        loader.Load(CarImage.FromBytes("a"u8), _light);
        loader.Load(CarImage.FromBytes("c"u8), _light);

        loader.Load(CarImage.FromBytes("a"u8), _light);
        Assert.Equal(3, _platform.Decodes);
        loader.Load(CarImage.FromBytes("b"u8), _light);
        Assert.Equal(4, _platform.Decodes);
    }

    [Fact]
    public async Task Uri_and_stream_load_asynchronously()
    {
        var uri = _loader.Load(CarImage.FromUri(new Uri("https://example.com/p.png")), _light);
        var stream = _loader.Load(CarImage.FromStream(_ => Task.FromResult<Stream>(new MemoryStream("str"u8.ToArray()))), _light);

        Assert.Null(uri.Image);
        Assert.Equal("bytes:png@64", await uri.Pending!);
        Assert.Equal("bytes:str@64", await stream.Pending!);
        Assert.Equal("bytes:png@64", _loader.Load(CarImage.FromUri(new Uri("https://example.com/p.png")), _light).Image);
    }

    // Review Focus 3: decoding images from the network and streams does not block the calling thread (the main thread)
    [Fact]
    public async Task Async_loads_run_off_the_calling_thread()
    {
        using var gate = new ManualResetEventSlim();
        _platform.DecodeGate = gate;

        var load = _loader.Load(CarImage.FromUri(new Uri("https://example.com/slow.png")), _light);
        gate.Set();

        Assert.Equal("bytes:png@64", await load.Pending!);
        Assert.True(_platform.DecodedAfterGate);
    }

    [Fact]
    public async Task Concurrent_requests_share_one_download()
    {
        _http.Release = new TaskCompletionSource();
        var image = CarImage.FromUri(new Uri("https://example.com/shared.png"));

        var first = _loader.Load(image, _light);
        var second = _loader.Load(image, _light);
        _http.Release.SetResult();

        Assert.Equal(await first.Pending!, await second.Pending!);
        Assert.Equal(1, _http.Requests);
    }

    [Fact]
    public async Task Failed_downloads_yield_no_image_and_log()
    {
        _http.Respond = uri => uri.AbsolutePath.Contains("bad", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bad image") }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Null(await _loader.Load(CarImage.FromUri(new Uri("https://example.com/404.png")), _light).Pending!);
        Assert.Null(await _loader.Load(CarImage.FromUri(new Uri("https://example.com/bad.png")), _light).Pending!);
        Assert.Null(await _loader.Load(CarImage.FromStream(_ => throw new IOException("disk")), _light).Pending!);
        Assert.Null(_loader.Load(CarImage.FromFile("missing.png"), _light).Image);
        Assert.Null(_loader.Load(CarImage.FromBytes("bad"u8), _light).Image);
        Assert.Equal(5, _logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Oversized_downloads_are_rejected()
    {
        var big = new byte[CarImageLoader<string>.MaxBytes + 1];
        _http.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(big) };

        Assert.Null(await _loader.Load(CarImage.FromUri(new Uri("https://example.com/huge.png")), _light).Pending!);
        Assert.Null(await _loader.Load(CarImage.FromStream(_ => Task.FromResult<Stream>(new MemoryStream(big))), _light).Pending!);
        Assert.Equal(0, _platform.Decodes);
        Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task Unsupported_scheme_yields_no_image()
    {
        var load = _loader.Load(CarImage.FromUri(new Uri("ftp://example.com/p.png")), _light);

        Assert.Null(load.Image);
        Assert.Null(load.Pending is null ? null : await load.Pending);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal(0, _http.Requests);
    }

    [Fact]
    public async Task File_uris_are_read_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"carkit-{Guid.NewGuid():N}.png");
        await File.WriteAllTextAsync(path, "disk", TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal("bytes:disk@64", await _loader.Load(CarImage.FromUri(new Uri(path)), _light).Pending!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dark_variant_is_selected_by_style()
    {
        var image = CarImage.FromFile("light.png").WithDarkVariant(CarImage.FromFile("dark.png"));

        Assert.Equal("file:light.png@64", _loader.Load(image, _light).Image);
        Assert.Equal("file:dark.png@64", _loader.Load(image, _light with { Style = CarContentStyle.Dark }).Image);
    }

    [Fact]
    public async Task Dark_variant_is_combined()
    {
        using var loader = new CarImageLoader<string>(_platform, _logger, combineDarkVariants: true, _http);
        var image = CarImage.FromFile("light.png").WithDarkVariant(CarImage.FromFile("dark.png"));
        var remote = CarImage.FromFile("light.png").WithDarkVariant(CarImage.FromUri(new Uri("https://example.com/dark.png")));

        Assert.Equal("asset(file:light.png@64|file:dark.png@64)@Dark", loader.Load(image, _light with { Style = CarContentStyle.Dark }).Image);
        Assert.Equal("asset(file:light.png@64|bytes:png@64)@Light", await loader.Load(remote, _light).Pending!);
    }

    [Fact]
    public void Tint_is_applied_after_decoding()
    {
        var image = CarImage.FromFile("icon.png") with { Tint = new CarColor(0xFF, 0x00, 0x10) };
        var dark = image.WithDarkVariant(CarImage.FromFile("icon-dark.png"));

        Assert.Equal("file:icon.png@64+tint:FF0010", _loader.Load(image, _light).Image);
        Assert.Equal("file:icon-dark.png@64+tint:FF0010", _loader.Load(dark, _light with { Style = CarContentStyle.Dark }).Image);
    }

    [Fact]
    public async Task Dispose_cancels_pending_loads()
    {
        var never = new TaskCompletionSource<Stream>();
        var load = _loader.Load(CarImage.FromStream(ct =>
        {
            ct.Register(() => never.TrySetCanceled(ct));
            return never.Task;
        }), _light);

        _loader.Dispose();

        Assert.Null(await load.Pending!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Null(_loader.Load(CarImage.FromUri(new Uri("https://example.com/after.png")), _light).Pending);
    }

    [Fact]
    public void Android_resource_names_follow_maui()
    {
        Assert.Equal("station_icon", CarImageNames.AndroidResourceName("Station-Icon.PNG"));
        Assert.Equal("a_b", CarImageNames.AndroidResourceName("img/a b.svg"));
        Assert.Equal("dotnet_bot", CarImageNames.AndroidResourceName("dotnet_bot.png"));
    }

    [Fact]
    public void Ios_candidates_include_png_for_svg()
    {
        Assert.Equal(["img/station.png", "station.png"], CarImageNames.IosBundleCandidates("img/station.svg"));
        Assert.Equal(["a.png"], CarImageNames.IosBundleCandidates("a.png"));
    }
}
