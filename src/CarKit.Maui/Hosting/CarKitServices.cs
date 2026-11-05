using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace CarKit.Maui;

// The CarPlay scene and CarAppService start without the phone UI: MAUI creates the MauiApp in
// MauiUIApplicationDelegate.WillFinishLaunching (iOS) and MauiApplication.OnCreate (Android), so the services exist.
internal static class CarKitServices
{
    public static CarKitRuntime ResolveRuntime(IPlatformApplication? application)
    {
        if (application is null)
        {
            throw new InvalidOperationException(
                "CarKit: IPlatformApplication.Current is null, so the car connected before MAUI started. "
                + "The app delegate must derive from MauiUIApplicationDelegate (iOS) and the application from MauiApplication (Android).");
        }

        // MauiUIApplicationDelegate.Services is null until WillFinishLaunching creates the MauiApp
        var services = (IServiceProvider?)application.Services
            ?? throw new InvalidOperationException("CarKit: MauiApp has not been created yet (IPlatformApplication.Current.Services is null).");

        return services.GetService<CarKitRuntime>()
            ?? throw new InvalidOperationException("CarKit is not registered: call builder.UseCarKit(...) in MauiProgram.CreateMauiApp.");
    }
}
