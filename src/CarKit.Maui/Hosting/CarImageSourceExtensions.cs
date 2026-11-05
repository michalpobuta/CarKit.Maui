using Microsoft.Maui.Controls;

namespace CarKit.Maui;

/// <summary>Converts MAUI image sources into car images.</summary>
public static class CarImageSourceExtensions
{
    /// <summary>
    /// Converts a MAUI <see cref="ImageSource"/> into a <see cref="CarImage"/>: a file becomes <see cref="CarImage.FromFile(string)"/>,
    /// a URI <see cref="CarImage.FromUri(Uri)"/> and a stream <see cref="CarImage.FromStream"/>.
    /// </summary>
    /// <param name="source">The image source.</param>
    /// <returns>The car image.</returns>
    /// <exception cref="NotSupportedException">The source type, such as <see cref="FontImageSource"/>, has no car equivalent.</exception>
    public static CarImage ToCarImage(this ImageSource source) => source switch
    {
        null => throw new ArgumentNullException(nameof(source)),
        FileImageSource file => CarImage.FromFile(file.File),
        UriImageSource uri => CarImage.FromUri(uri.Uri),
        StreamImageSource stream => CarImage.FromStream(token => stream.Stream(token)),
        _ => throw new NotSupportedException($"{source.GetType().Name} cannot be shown in the car; use a file, URI or stream image source."),
    };
}
