using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Media;

// Everything that auto-browse, the Media3 service and the CarPlay bridge need: library, controller (state and
// commands), options
internal sealed class CarMediaRuntime(
    ICarMediaLibrary library,
    CarMediaController controller,
    CarMediaOptions options,
    ICarErrorHandler errors,
    ILogger logger)
{
    public ICarMediaLibrary Library { get; } = library;

    public CarMediaController Controller { get; } = controller;

    public CarMediaOptions Options { get; } = options;

    public ICarErrorHandler Errors { get; } = errors;

    public ILogger Logger { get; } = logger;
}
