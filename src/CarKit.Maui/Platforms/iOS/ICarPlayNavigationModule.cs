using CarPlay;
using UIKit;

namespace CarKit.Maui;

// Stage 8: the Navigation module's attachment point in CarPlay hosting (spec: "iOS hosting", "Navigation module
// decisions (Stage 8)"). The module registers an implementation in DI; the CarKit scenes (main, Dashboard, cluster)
// pass it windows and background navigation events. Called on the main thread from UIKit/CarPlay callbacks; an
// exception goes to ICarErrorHandler.
internal interface ICarPlayNavigationModule
{
    // Main scene: the CPWindow (the DidConnect variant with a window, navigation apps only) before the app's
    // OnConnectedAsync
    void OnConnected(Renderers.CarPlayRenderContext context, CPWindow? window);

    void OnDisconnected(Renderers.CarPlayRenderContext context);

    // CPTemplateApplicationSceneDelegate.ContentStyleDidChange of the main scene (day/night) — the CarScreen surface
    void OnContentStyleChanged(Renderers.CarPlayRenderContext context, CarContentStyle style);

    // CPTemplateApplicationSceneDelegate.DidSelect: a maneuver or alert notification selected while the app is in the
    // background
    void OnManeuverSelected(CPManeuver maneuver);

    void OnNavigationAlertSelected(CPNavigationAlert alert);

    void OnDashboardConnected(CPDashboardController controller, UIWindow window);

    void OnDashboardDisconnected(UIWindow window);

    [System.Runtime.Versioning.SupportedOSPlatform("ios15.4")]
    void OnInstrumentClusterConnected(CPTemplateApplicationInstrumentClusterScene scene, CPInstrumentClusterController controller);

    [System.Runtime.Versioning.SupportedOSPlatform("ios15.4")]
    void OnInstrumentClusterDisconnected(CPInstrumentClusterController controller);

    [System.Runtime.Versioning.SupportedOSPlatform("ios15.4")]
    void OnInstrumentClusterContentStyleChanged(UIUserInterfaceStyle style);
}

// The Navigation module for scenes without a CarPlayHost (Dashboard, cluster): from the app services, and without MAUI
// or the module — null with a log
internal static class CarPlayNavigationModules
{
    public static ICarPlayNavigationModule? Resolve(string scene)
    {
        try
        {
            var runtime = CarKitServices.ResolveRuntime(Microsoft.Maui.IPlatformApplication.Current);
            if (Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<ICarPlayNavigationModule>(runtime.Services) is { } module)
            {
                return module;
            }

            CarKitHostingLog.NavigationModuleMissing(runtime.Logger, scene);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }

        return null;
    }

    public static void Run(ICarPlayNavigationModule? module, Action<ICarPlayNavigationModule> call, string operation)
    {
        if (module is null)
        {
            return;
        }

        try
        {
            call(module);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try
            {
                CarKitServices.ResolveRuntime(Microsoft.Maui.IPlatformApplication.Current).ErrorHandler.HandleError(ex, new CarErrorContext(operation, null));
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine(ex);
            }
        }
    }
}
