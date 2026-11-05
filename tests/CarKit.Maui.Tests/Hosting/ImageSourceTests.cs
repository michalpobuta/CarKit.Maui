using Microsoft.Maui.Controls;
using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public class ImageSourceTests
{
    [Fact]
    public async Task File_uri_and_stream_sources_convert()
    {
        var file = Assert.IsType<CarFileImage>(new FileImageSource { File = "station.png" }.ToCarImage());
        var uri = Assert.IsType<CarUriImage>(new UriImageSource { Uri = new Uri("https://example.com/p.png") }.ToCarImage());
        var stream = Assert.IsType<CarStreamImage>(ImageSource.FromStream(() => new MemoryStream([1, 2, 3])).ToCarImage());

        Assert.Equal("station.png", file.Path);
        Assert.Equal(new Uri("https://example.com/p.png"), uri.Uri);
        await using var opened = await stream.OpenAsync(CancellationToken.None);
        Assert.Equal(3, opened.Length);
    }

    [Fact]
    public void Font_source_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() => new FontImageSource { Glyph = "x" }.ToCarImage());
        Assert.Throws<ArgumentNullException>(() => ((ImageSource)null!).ToCarImage());
    }
}
