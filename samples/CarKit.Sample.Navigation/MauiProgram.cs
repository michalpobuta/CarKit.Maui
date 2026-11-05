using CarKit.Maui;
using CarKit.Maui.Navigation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace CarKit.Sample.Navigation;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<SampleMapState>();
        builder.Services.AddSingleton(sp => new SampleRouteSimulator(sp.GetRequiredService<ICarNavigationSession>(), sp.GetRequiredService<ICarVoicePrompts>()));
        builder.Services.AddSingleton(sp => new SampleMapRenderer(
            sp.GetRequiredService<SampleRouteSimulator>(), sp.GetRequiredService<SampleMapState>(), () => sp.GetRequiredService<ICarMapSurfaces>().CarScreen.ContentStyle));
        builder.Services.AddSingleton<MainPage>();
        // Surface content before the car connects (CarPlay also connects when the app starts)
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, SampleMapInitializer>());

        builder.UseCarKit(car => car.UseHandler<SampleNavigationCarHandler>().UseNavigation(nav =>
        {
            nav.UseHandler<SampleNavigationHandler>();
            nav.VoicePromptLanguage = "en-GB";
            nav.LogFrameTimes = true;
            // Stage 9: sharing the route with the car (CarPlay iOS 26.4, cars with this feature)
            nav.ShareRoutesWithCar = true;
        }));

#if DEBUG
        builder.Logging.AddDebug();
#endif
#if ANDROID
        builder.Logging.AddProvider(new LogcatLoggerProvider());
#elif IOS
        builder.Logging.AddProvider(new ConsoleLoggerProvider());
#endif

        return builder.Build();
    }
}

internal static partial class SampleLog
{
    [LoggerMessage(900, LogLevel.Information, "CarKit.Sample navigation: {Message}")]
    public static partial void Step(ILogger logger, string message);
}

// The sample map on the car screen, Dashboard and cluster; map gestures and buttons move the same map; the simulation
// refreshes frames
internal sealed class SampleMapInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        var surfaces = services.GetRequiredService<ICarMapSurfaces>();
        var renderer = services.GetRequiredService<SampleMapRenderer>();
        var map = services.GetRequiredService<SampleMapState>();
        var simulator = services.GetRequiredService<SampleRouteSimulator>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CarKit.Sample");
        foreach (var surface in new[] { surfaces.CarScreen, surfaces.Dashboard, surfaces.InstrumentCluster })
        {
            surface.UseRenderer(renderer);
            map.Track(surface);
        }

        simulator.Progress += (_, message) =>
        {
            SampleLog.Step(logger, message);
            surfaces.CarScreen.Invalidate();
            surfaces.Dashboard.Invalidate();
            surfaces.InstrumentCluster.Invalidate();
        };
    }
}
