using CarPlay;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace CarKit.Maui;

/// <summary>
/// The CarPlay Dashboard scene delegate named in the generated scene manifest (<c>CarKitIosDashboard</c>). It hands the
/// dashboard window to the navigation module, which draws the map surface there.
/// </summary>
[Register("CarKitDashboardSceneDelegate")]
public sealed class CarKitDashboardSceneDelegate : CPTemplateApplicationDashboardSceneDelegate
{
    private ICarPlayNavigationModule? _module;

    /// <summary>Creates the delegate; UIKit creates it from the scene manifest.</summary>
    public CarKitDashboardSceneDelegate()
    {
    }

    private CarKitDashboardSceneDelegate(NativeHandle handle)
        : base(handle)
    {
    }

    /// <inheritdoc />
    public override void DidConnectDashboardController(CPTemplateApplicationDashboardScene templateApplicationDashboardScene, CPDashboardController dashboardController, UIWindow window)
    {
        _module = CarPlayNavigationModules.Resolve("CarPlay Dashboard");
        CarPlayNavigationModules.Run(_module, module => module.OnDashboardConnected(dashboardController, window), "Navigation.DashboardConnected");
    }

    /// <inheritdoc />
    public override void DidDisconnectDashboardController(CPTemplateApplicationDashboardScene templateApplicationDashboardScene, CPDashboardController dashboardController, UIWindow window)
    {
        CarPlayNavigationModules.Run(_module, module => module.OnDashboardDisconnected(window), "Navigation.DashboardDisconnected");
        _module = null;
    }
}
