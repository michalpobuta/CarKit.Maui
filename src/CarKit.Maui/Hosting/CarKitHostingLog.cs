using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Hosting messages (category CarKit.Hosting), allocation-free and AOT-compatible
internal static partial class CarKitHostingLog
{
    [LoggerMessage(100, LogLevel.Information, "CarKit car app service created; CarKit services resolved from MauiApp")]
    public static partial void CarAppServiceCreated(ILogger logger);

    // Stage 8: a second-screen scene (Dashboard, cluster) or an Android cluster session without the Navigation module
    [LoggerMessage(101, LogLevel.Warning, "CarKit {Screen} connected, but the app has no CarKit.Maui.Navigation module (car.UseNavigation); nothing is drawn")]
    public static partial void NavigationModuleMissing(ILogger logger, string screen);
}
