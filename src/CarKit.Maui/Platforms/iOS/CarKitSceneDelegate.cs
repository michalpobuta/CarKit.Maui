using CarPlay;
using Foundation;
using Microsoft.Maui;
using ObjCRuntime;
using UIKit;

namespace CarKit.Maui;

/// <summary>
/// The CarPlay scene delegate named in the generated scene manifest. It starts a car session when CarPlay
/// connects, also while the phone is locked and no phone scene exists, and ends it when CarPlay disconnects.
/// </summary>
[Register("CarKitSceneDelegate")]
public sealed class CarKitSceneDelegate : CPTemplateApplicationSceneDelegate
{
    private CarPlayHost? _host;
    private CarKitConnection? _connection;

    /// <summary>Creates the delegate; UIKit creates it from the scene manifest.</summary>
    public CarKitSceneDelegate()
    {
    }

    private CarKitSceneDelegate(NativeHandle handle)
        : base(handle)
    {
    }

    // The CarPlay window of navigation apps; the host passes it to the Navigation module (ICarPlayNavigationModule, Stage 8)
    internal CPWindow? CarWindow { get; private set; }

    /// <inheritdoc />
    public override void DidConnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController) =>
        Connect(templateApplicationScene, interfaceController, window: null);

    /// <inheritdoc />
    public override void DidConnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController, CPWindow window) =>
        Connect(templateApplicationScene, interfaceController, window);

    /// <inheritdoc />
    public override void DidDisconnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController) => Disconnect();

    /// <inheritdoc />
    public override void DidDisconnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController, CPWindow window) => Disconnect();

    /// <inheritdoc />
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.4")]
    public override void ContentStyleDidChange(UIUserInterfaceStyle contentStyle) => _host?.OnContentStyleChanged(contentStyle);

    /// <inheritdoc />
    public override void DidSelect(CPTemplateApplicationScene templateApplicationScene, CPManeuver maneuver) => _host?.OnManeuverSelected(maneuver);

    /// <inheritdoc />
    public override void DidSelect(CPTemplateApplicationScene templateApplicationScene, CPNavigationAlert navigationAlert) => _host?.OnNavigationAlertSelected(navigationAlert);

    private void Connect(CPTemplateApplicationScene scene, CPInterfaceController controller, CPWindow? window)
    {
        Disconnect();
        CarKitRuntime runtime;
        try
        {
            // IPlatformApplication.Current and Services exist since WillFinishLaunching, also without a phone scene
            runtime = CarKitServices.ResolveRuntime(IPlatformApplication.Current);
        }
        catch (InvalidOperationException ex)
        {
            // Without CarKitRuntime there is no ICarErrorHandler; an exception from a UIKit callback would crash the app
            Console.Error.WriteLine(ex.Message);
            return;
        }

        CarWindow = window;
        _host = new CarPlayHost(controller, scene, runtime, window);
        _connection = runtime.Connect(_host);
        _host.Attach(_connection.Session);
        _connection.StartAsync().Forget(runtime.ErrorHandler, "OnConnected");
    }

    private void Disconnect()
    {
        var connection = _connection;
        _host?.Close();
        _host = null;
        _connection = null;
        CarWindow = null;
        connection?.DisconnectAsync().Forget(NullErrors.Instance, "OnDisconnected");
    }

    // DisconnectAsync reports errors itself; here only in case of an infrastructure error
    private sealed class NullErrors : ICarErrorHandler
    {
        public static readonly NullErrors Instance = new();

        public void HandleError(Exception exception, CarErrorContext context) => Console.Error.WriteLine(exception);
    }
}
