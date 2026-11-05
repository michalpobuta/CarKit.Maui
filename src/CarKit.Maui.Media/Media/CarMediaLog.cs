using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Media;

// Media module messages (category CarKit.Media), allocation-free and AOT-compatible
internal static partial class CarMediaLog
{
    [LoggerMessage(300, LogLevel.Warning, "CarKit could not load the media library for '{ParentId}'")]
    public static partial void LibraryFailed(ILogger logger, string parentId, Exception exception);

    [LoggerMessage(301, LogLevel.Warning, "CarKit shows no artwork for media item '{ItemId}': {Reason}")]
    public static partial void ArtworkSkipped(ILogger logger, string itemId, string reason);

    [LoggerMessage(302, LogLevel.Information, "CarKit media: {Message}")]
    public static partial void Info(ILogger logger, string message);

    [LoggerMessage(303, LogLevel.Warning, "CarKit media: {Message}")]
    public static partial void Warning(ILogger logger, string message, Exception? exception);
}
