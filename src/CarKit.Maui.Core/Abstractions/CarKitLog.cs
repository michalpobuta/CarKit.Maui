using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Log messages as LoggerMessage (allocation-free, AOT-compatible)
internal static partial class CarKitLog
{
    [LoggerMessage(1, LogLevel.Warning, "CarKit validation: {Problem}")]
    public static partial void ValidationWarning(ILogger logger, string problem, Exception exception);

    [LoggerMessage(2, LogLevel.Debug, "CarKit truncated {Limit} from {Actual} to {Maximum}")]
    public static partial void Truncated(ILogger logger, CarLimitKind limit, int actual, int maximum);

    [LoggerMessage(3, LogLevel.Debug, "CarKit native call: {Operation} {TemplateKind} {TemplateId}")]
    public static partial void NativeCall(ILogger logger, string operation, CarTemplateKind? templateKind, string? templateId);

    [LoggerMessage(4, LogLevel.Error, "CarKit handler failed during {Operation}")]
    public static partial void HandlerFailed(ILogger logger, string operation, Exception exception);

    // Stage 8: a report from the car for an object outside the current navigation (e.g. a maneuver from before an
    // update) — without a handler
    [LoggerMessage(5, LogLevel.Debug, "CarKit navigation ignored {Operation} for '{Id}', which is not part of the current navigation")]
    public static partial void NavigationTargetUnknown(ILogger logger, string operation, string? id);

    // Stage 9: feature gate — the template goes to the host without a feature that the platform, version or CarKit
    // asset does not have
    [LoggerMessage(6, LogLevel.Information, "CarKit shows {Kind} '{TemplateId}' without {Feature}: {Platform} {Version} or this CarKit build does not support it")]
    public static partial void FeatureUnavailable(ILogger logger, CarTemplateKind kind, string templateId, CarFeature feature, CarPlatform platform, string version);
}
