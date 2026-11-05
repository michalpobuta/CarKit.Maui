using System.Runtime.Versioning;
using CarKit.Maui.Renderers;
using CarPlay;
using UIKit;

namespace CarKit.Maui.Navigation;

// The Navigation module in CarPlay hosting (ICarPlayNavigationModule): connection of the main scene (window →
// CarScreen, navigation sink on CPMapTemplate), Dashboard and cluster (windows → surfaces), background navigation
// (DidSelect). DI singleton; main thread.
internal sealed class CarPlayNavigationModule(CarNavigationRuntime runtime) : ICarPlayNavigationModule, IDisposable
{
    // Navigation alerts → model Id (CPNavigationAlert has no UserInfo). The key is the native object handle, and the
    // entry holds the peer strongly until the alert is dismissed: CarPlay hands the native object back in the delegate,
    // and after a GC the managed peer would be new (I4). At most AlertLimit entries (without a CarPlay connection
    // DidDismissNavigationAlert is not called)
    private const int AlertLimit = 8;
    private readonly List<(CPNavigationAlert Native, string Id)> _alerts = [];
    private readonly Dictionary<CarPlayRenderContext, Connection> _connections = new(ReferenceEqualityComparer.Instance);
    private CarSurfaceWindowHost? _dashboard;
    private CarSurfaceWindowHost? _cluster;
    private InstrumentClusterDelegate? _clusterDelegate;
    private CarContentStyle _clusterStyle;

    public CarNavigationRuntime Runtime { get; } = runtime;

    public void Dispose()
    {
        _clusterDelegate?.Dispose();
        _dashboard?.Dispose();
        _cluster?.Dispose();
    }

    public void OnConnected(CarPlayRenderContext context, CPWindow? window)
    {
        var sink = new CarPlayNavigationSink(this, context);
        var connection = new Connection(sink, window is null ? null : new CarSurfaceWindowHost(window, Runtime.Surfaces.CarScreen, Runtime, context.Style));
        _connections[context] = connection;
        connection.Window?.Attach();
        Runtime.Navigation.AttachAsync(sink).ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    // Disconnected while attaching: detach at once
                    connection.SetAttachment(task.Result);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void OnDisconnected(CarPlayRenderContext context)
    {
        if (_connections.Remove(context, out var connection))
        {
            connection.Close();
        }

        // Alerts live on the connection's map template
        if (_connections.Count == 0)
        {
            _alerts.Clear();
        }
    }

    public void OnContentStyleChanged(CarPlayRenderContext context, CarContentStyle style)
    {
        if (_connections.ContainsKey(context))
        {
            Runtime.Surfaces.CarScreen.SetContentStyle(style);
        }
    }

    public void OnManeuverSelected(CPManeuver maneuver) =>
        Runtime.Navigation.OnManeuverSelectedAsync(maneuver.UserInfo?.ToString() ?? string.Empty).Forget(Runtime.Errors, "Navigation.ManeuverSelected");

    public void OnNavigationAlertSelected(CPNavigationAlert alert)
    {
        if (AlertId(alert) is { } id)
        {
            Runtime.Navigation.OnAlertSelectedAsync(id).Forget(Runtime.Errors, "Navigation.NavigationAlertSelected");
        }
    }

    public void OnDashboardConnected(CPDashboardController controller, UIWindow window)
    {
        _dashboard?.Detach();
        // The Dashboard scene has no contentStyle: style from the window traits, changes from the container traits
        // (followTraits)
        _dashboard = new CarSurfaceWindowHost(window, Runtime.Surfaces.Dashboard, Runtime, CarSurfaceWindowHost.StyleOf(window.TraitCollection), followTraits: true);
        _dashboard.Attach();
    }

    public void OnDashboardDisconnected(UIWindow window)
    {
        _dashboard?.Detach();
        _dashboard = null;
    }

    [SupportedOSPlatform("ios15.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    public void OnInstrumentClusterConnected(CPTemplateApplicationInstrumentClusterScene scene, CPInstrumentClusterController controller)
    {
        _clusterStyle = scene.ContentStyle == UIUserInterfaceStyle.Dark ? CarContentStyle.Dark : CarContentStyle.Light;
        _clusterDelegate = new InstrumentClusterDelegate(this);
        controller.Delegate = _clusterDelegate;
        if (controller.InstrumentClusterWindow is { } window)
        {
            OnClusterWindow(window);
        }
    }

    [SupportedOSPlatform("ios15.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    public void OnInstrumentClusterDisconnected(CPInstrumentClusterController controller)
    {
        controller.WeakDelegate = null;
        _clusterDelegate = null;
        OnClusterWindowGone();
    }

    [SupportedOSPlatform("ios15.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    public void OnInstrumentClusterContentStyleChanged(UIUserInterfaceStyle style)
    {
        _clusterStyle = style == UIUserInterfaceStyle.Dark ? CarContentStyle.Dark : CarContentStyle.Light;
        Runtime.Surfaces.InstrumentCluster.SetContentStyle(_clusterStyle);
        Runtime.Surfaces.InstrumentCluster.Invalidate();
    }

    // From the map template renderer: the newest CPMapTemplate of the connection shows navigation
    public void OnMapTemplateRendered(CarPlayRenderContext context, CPMapTemplate map, CarPlayTemplateState state)
    {
        if (_connections.TryGetValue(context, out var connection))
        {
            connection.Sink.UseMap(map, state);
        }
    }

    // CarPlay ended guidance itself (DidCancelNavigation): the native session no longer exists
    public void OnNavigationCancelledByCar(CarPlayRenderContext context)
    {
        if (_connections.TryGetValue(context, out var connection))
        {
            connection.Sink.ForgetSession();
        }
    }

    // Device tests: the connection sink and cluster zoom (CPInstrumentClusterController delegate path)
    internal CarPlayNavigationSink? SinkFor(CarPlayRenderContext context) => _connections.TryGetValue(context, out var connection) ? connection.Sink : null;

    internal void ZoomCluster(bool zoomIn)
    {
        var surface = Runtime.Surfaces.InstrumentCluster;
        surface.RaiseGesture(CarMapGestureTranslator.Zoom(zoomIn, surface.Size));
    }

    public void RememberAlert(CPNavigationAlert native, string id)
    {
        _alerts.RemoveAll(entry => entry.Native.Handle == native.Handle);
        _alerts.Add((native, id));
        if (_alerts.Count > AlertLimit)
        {
            _alerts.RemoveAt(0);
        }
    }

    // Alert dismissed (DidDismissNavigationAlert): later delegate calls do not concern it
    public string? ForgetAlert(CPNavigationAlert native)
    {
        var id = AlertId(native);
        _alerts.RemoveAll(entry => entry.Native.Handle == native.Handle);
        return id;
    }

    public string? AlertId(CPNavigationAlert native)
    {
        foreach (var entry in _alerts)
        {
            if (entry.Native.Handle == native.Handle)
            {
                return entry.Id;
            }
        }

        return null;
    }

    internal void OnClusterWindow(UIWindow window)
    {
        _cluster?.Detach();
        _cluster = new CarSurfaceWindowHost(window, Runtime.Surfaces.InstrumentCluster, Runtime, _clusterStyle);
        _cluster.Attach();
    }

    internal void OnClusterWindowGone()
    {
        _cluster?.Detach();
        _cluster = null;
    }

    private sealed class Connection(CarPlayNavigationSink sink, CarSurfaceWindowHost? window)
    {
        private readonly Lock _lock = new();
        private IDisposable? _attachment;
        private bool _closed;

        public CarPlayNavigationSink Sink { get; } = sink;

        public CarSurfaceWindowHost? Window { get; } = window;

        public void SetAttachment(IDisposable attachment)
        {
            lock (_lock)
            {
                if (_closed)
                {
                    attachment.Dispose();
                    return;
                }

                _attachment = attachment;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _closed = true;
                _attachment?.Dispose();
                _attachment = null;
            }

            Sink.Close();
            Window?.Detach();
        }
    }

    // CPInstrumentClusterController delegate (iOS 15.4): the cluster window and steering wheel zoom buttons
    [SupportedOSPlatform("ios15.4")]
    [UnsupportedOSPlatform("maccatalyst")]
    private sealed class InstrumentClusterDelegate(CarPlayNavigationModule module) : CPInstrumentClusterControllerDelegate
    {
        public override void DidConnectWindow(UIWindow instrumentClusterWindow) => module.OnClusterWindow(instrumentClusterWindow);

        public override void DidDisconnectWindow(UIWindow instrumentClusterWindow) => module.OnClusterWindowGone();

        public override void DidZoomIn(CPInstrumentClusterController instrumentClusterController) => module.ZoomCluster(zoomIn: true);

        public override void DidZoomOut(CPInstrumentClusterController instrumentClusterController) => module.ZoomCluster(zoomIn: false);
    }
}
