using System.Runtime.Versioning;
using CarPlay;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace CarKit.Maui;

/// <summary>
/// The CarPlay instrument cluster scene delegate named in the generated scene manifest (<c>CarKitIosInstrumentCluster</c>,
/// iOS 15.4). It hands the instrument cluster controller to the navigation module, which draws the map surface in the
/// cluster window and handles zoom.
/// </summary>
[Register("CarKitInstrumentClusterSceneDelegate")]
[SupportedOSPlatform("ios15.4")]
[UnsupportedOSPlatform("maccatalyst")]
public sealed class CarKitInstrumentClusterSceneDelegate : CPTemplateApplicationInstrumentClusterSceneDelegate
{
    private ICarPlayNavigationModule? _module;

    /// <summary>Creates the delegate; UIKit creates it from the scene manifest.</summary>
    public CarKitInstrumentClusterSceneDelegate()
    {
    }

    private CarKitInstrumentClusterSceneDelegate(NativeHandle handle)
        : base(handle)
    {
    }

    /// <inheritdoc />
    public override void DidConnectInstrumentClusterController(CPTemplateApplicationInstrumentClusterScene templateApplicationInstrumentClusterScene, CPInstrumentClusterController instrumentClusterController)
    {
        _module = CarPlayNavigationModules.Resolve("CarPlay instrument cluster");
        CarPlayNavigationModules.Run(_module, module => module.OnInstrumentClusterConnected(templateApplicationInstrumentClusterScene, instrumentClusterController), "Navigation.InstrumentClusterConnected");
    }

    /// <inheritdoc />
    public override void DidDisconnectInstrumentClusterController(CPTemplateApplicationInstrumentClusterScene templateApplicationInstrumentClusterScene, CPInstrumentClusterController instrumentClusterController)
    {
        CarPlayNavigationModules.Run(_module, module => module.OnInstrumentClusterDisconnected(instrumentClusterController), "Navigation.InstrumentClusterDisconnected");
        _module = null;
    }

    /// <inheritdoc />
    public override void ContentStyleDidChange(UIUserInterfaceStyle contentStyle) =>
        CarPlayNavigationModules.Run(_module, module => module.OnInstrumentClusterContentStyleChanged(contentStyle), "Navigation.InstrumentClusterContentStyle");
}
