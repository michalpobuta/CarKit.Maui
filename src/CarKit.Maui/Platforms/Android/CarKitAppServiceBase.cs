using Android.Util;
using AndroidX.Car.App;
using AndroidX.Car.App.Validation;
using CarKit.Maui.Renderers;
using Microsoft.Maui;

namespace CarKit.Maui;

/// <summary>
/// The base of the car app service the Android Auto host binds to. The CarKit source generator derives
/// <c>CarKitGeneratedAppService</c> from it in the app, with the intent filter for the declared categories.
/// </summary>
public abstract class CarKitAppServiceBase : CarAppService
{
    /// <summary>Creates the service; Android creates it when a host binds, also without an activity.</summary>
    protected CarKitAppServiceBase()
    {
    }

    /// <summary>Resolves the CarKit services when Android creates the service, also in a process started without an activity.</summary>
    public override void OnCreate()
    {
        base.OnCreate();
        try
        {
            // Spec: the car session starts without the phone UI; MauiApplication.OnCreate has already created the MauiApp
            CarKitHostingLog.CarAppServiceCreated(Runtime.Logger);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("CarKit", ex.Message);
        }
    }

    /// <summary>Returns which hosts may bind: every host in debuggable builds, otherwise the allowed hosts.</summary>
    /// <returns>The host validator.</returns>
    public override HostValidator CreateHostValidator()
    {
        try
        {
            return CarKitHostValidator.Create(this, Runtime.Android);
        }
        catch (InvalidOperationException ex)
        {
            // Without the CarKit configuration no host gets a session; an exception from a Java callback would crash
            // the process
            Log.Error("CarKit", ex.Message);
            return new HostValidator.Builder(this).Build()!;
        }
    }

    /// <summary>Creates the session for a connected car screen.</summary>
    /// <returns>The session.</returns>
    public override Session OnCreateSession() => new CarKitSession(Runtime);

    /// <summary>
    /// Creates the session for a car display: the cluster display (Car App API 6, <c>CarKitAndroidCluster</c>) gets the
    /// navigation module's cluster session, every other display the regular session.
    /// </summary>
    /// <param name="sessionInfo">The display the host creates the session for.</param>
    /// <returns>The session.</returns>
    public override Session OnCreateSession(SessionInfo? sessionInfo)
    {
        if (sessionInfo?.DisplayType == SessionInfo.DisplayTypeCluster)
        {
            try
            {
                if (Runtime.Services.GetService(typeof(ICarAppClusterSessionFactory)) is ICarAppClusterSessionFactory factory)
                {
                    return factory.CreateClusterSession();
                }

                CarKitHostingLog.NavigationModuleMissing(Runtime.Logger, "Android Auto cluster display");
            }
            catch (InvalidOperationException ex)
            {
                Log.Error("CarKit", ex.Message);
            }
        }

        return OnCreateSession();
    }

    // MauiApplication.OnCreate created the MauiApp before Android created the service
    private static CarKitRuntime Runtime => CarKitServices.ResolveRuntime(IPlatformApplication.Current);
}
