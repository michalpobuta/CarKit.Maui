using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Navigation;

// Navigation module messages (category CarKit.Navigation), allocation-free and AOT-compatible
internal static partial class CarNavigationLog
{
    [LoggerMessage(800, LogLevel.Information, "CarKit map frames {Surface} ({Mode}): {Count} frames, average {Average:0.0} ms, median {Median:0.0} ms, p95 {P95:0.0} ms, max {Max:0.0} ms, over 16.7 ms {Slow}, over 33.3 ms {VerySlow}")]
    public static partial void FrameTimes(
        ILogger logger, CarMapSurfaceKind surface, string mode, int count, double average, double median, double p95, double max, int slow, int verySlow);

    [LoggerMessage(801, LogLevel.Warning, "Android Auto shows no lanes for maneuver '{ManeuverId}': it needs LanesImage with Lanes")]
    public static partial void LanesWithoutImage(ILogger logger, string maneuverId);

    [LoggerMessage(802, LogLevel.Warning, "Android Auto shows no navigation alert '{AlertId}': alerts need Car App API level {Required}, the host has {ApiLevel}")]
    public static partial void AlertUnavailable(ILogger logger, string alertId, int required, int apiLevel);

    [LoggerMessage(803, LogLevel.Information, "CarKit map surface {Surface} {State} ({Width}×{Height}, density {Density})")]
    public static partial void SurfaceChanged(ILogger logger, CarMapSurfaceKind surface, string state, float width, float height, float density);

    [LoggerMessage(804, LogLevel.Warning, "CarKit map surface {Surface} could not show its content")]
    public static partial void ContentFailed(ILogger logger, CarMapSurfaceKind surface, Exception exception);

    [LoggerMessage(805, LogLevel.Debug, "CarKit navigation: {Message}")]
    public static partial void Trace(ILogger logger, string message);
}
