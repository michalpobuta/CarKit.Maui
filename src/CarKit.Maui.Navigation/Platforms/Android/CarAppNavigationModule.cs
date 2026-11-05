using Android.Content;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Navigation;
using CarKit.Maui.Renderers;

namespace CarKit.Maui.Navigation;

// The Navigation module in the Car App Library (ICarAppModuleRenderer): CarMapTemplate renderer and POI with the app
// map, the connection (CarKitSurfaceCallback surface, NavigationManager, navigation sink), ACTION_NAVIGATE intents and
// the cluster session (ICarAppClusterSessionFactory). DI singleton; state per connection (render context). Main
// thread.
internal sealed class CarAppNavigationModule(CarNavigationRuntime runtime) : ICarAppModuleRenderer, ICarAppClusterSessionFactory
{
    private readonly Dictionary<CarAppRenderContext, CarAppNavigationConnection> _connections = new(ReferenceEqualityComparer.Instance);

    public CarNavigationRuntime Runtime { get; } = runtime;

    public bool CanRender(CarTemplate template, CarAppRenderContext context) => template switch
    {
        CarMapTemplate => true,
        CarPointOfInterestTemplate => Connection(context).UsesAppMapForPointsOfInterest,
        _ => false,
    };

    public string? TemplateType(CarTemplate template, CarAppRenderContext context) => template switch
    {
        CarMapTemplate => CarNavigationTemplates.MapTemplateType(Runtime.Navigation.Snapshot.State, context.ApiLevel),
        CarPointOfInterestTemplate when Connection(context).UsesAppMapForPointsOfInterest => CarAppTemplateTypes.MapWithContent,
        _ => null,
    };

    public CarAppRendered Render(CarTemplate template, CarAppRenderContext context, CarAppScreenState screen)
    {
        var connection = Connection(context);
        return template switch
        {
            CarMapTemplate map => connection.RenderMap(map, screen),
            CarPointOfInterestTemplate points => PointOfInterestMapRenderer.Render(points, context, screen),
            _ => throw new NotSupportedException($"The navigation module does not render {template.Kind}."),
        };
    }

    public void OnConnected(CarAppRenderContext context, Java.Lang.Object carContext)
    {
        var car = carContext.JavaCast<CarContext>()!;
        var connection = new CarAppNavigationConnection(this, context, car);
        _connections[context] = connection;
        connection.Start();
    }

    public void OnDisconnected(CarAppRenderContext context)
    {
        if (_connections.Remove(context, out var connection))
        {
            connection.Close();
        }
    }

    public void OnContentStyleChanged(CarAppRenderContext context, CarContentStyle style)
    {
        if (_connections.TryGetValue(context, out var connection))
        {
            connection.Surface?.SetContentStyle(style);
        }
    }

    // Navigation intents (Assistant, other apps): ACTION_NAVIGATE with geo: → OnNavigationRequestedAsync
    public void OnIntent(CarAppRenderContext context, Intent intent)
    {
        if (intent.Action == CarContext.ActionNavigate && intent.DataString is { } data && Uri.TryCreate(data, UriKind.Absolute, out var uri))
        {
            Runtime.Navigation.OnNavigationRequestedAsync(new CarNavigationRequest(uri)).Forget(Runtime.Errors, "Navigation.NavigationRequested");
        }
    }

    public AndroidX.Car.App.Session CreateClusterSession() => new CarKitClusterSession(Runtime);

    // Context without a connection (device tests render without a host): a connection without CarContext
    internal CarAppNavigationConnection Connection(CarAppRenderContext context)
    {
        if (!_connections.TryGetValue(context, out var connection))
        {
            connection = new CarAppNavigationConnection(this, context, carContext: null);
            _connections[context] = connection;
            connection.Start();
        }

        return connection;
    }
}

// One Android Auto connection: map screens (rebuilt on navigation change), the CarScreen surface, NavigationManager
internal sealed class CarAppNavigationConnection : ICarNavigationSink, IDisposable
{
    private readonly CarAppNavigationModule _module;
    private readonly CarAppRenderContext _context;
    private readonly CarContext? _carContext;
    private readonly List<CarAppScreenState> _mapScreens = [];
    private readonly Lock _lock = new();
    private CarSurfaceHost? _surface;
    private NavigationManager? _navigationManager;
    private IDisposable? _attachment;
    private bool _navigationStarted;
    private bool _closed;
    private int _nextAlertId = 1000;

    public CarAppNavigationConnection(CarAppNavigationModule module, CarAppRenderContext context, CarContext? carContext)
    {
        _module = module;
        _context = context;
        _carContext = carContext;
        // Choice fixed per connection (like List/Sectioned): API 7, a category with a map and CarScreen content at
        // connection time
        UsesAppMapForPointsOfInterest = CarNavigationTemplates.UsesAppMapForPointsOfInterest(
            context.ApiLevel, Runtime.Categories, Runtime.Surfaces.CarScreen.HasContent);
    }

    public bool UsesAppMapForPointsOfInterest { get; }

    // Maneuvers for which the warning about lanes without an image was already logged
    public HashSet<string> LanesWarned { get; } = [];

    internal bool NavigationStarted => _navigationStarted;

    internal CarSurfaceHost? Surface => _surface;

    private CarNavigationRuntime Runtime => _module.Runtime;

    public void Start()
    {
        if (_carContext is { } car)
        {
            // Map on a surface only in categories with ACCESS_SURFACE (targets)
            if (CarNavigationTemplates.HasMapSurface(Runtime.Categories))
            {
                _surface = new CarSurfaceHost(Runtime.Surfaces.CarScreen, Runtime, car, car.IsDarkMode ? CarContentStyle.Dark : CarContentStyle.Light);
                var appManager = car.GetCarService(CarContext.AppService)!.JavaCast<AppManager>()!;
                appManager.SetSurfaceCallback(new CarKitSurfaceCallback(_surface));
            }

            // NavigationManager is allowed only for navigation apps (NAVIGATION_TEMPLATES); callback before
            // navigationStarted
            if (Runtime.Categories.HasFlag(CarAppCategory.Navigation))
            {
                _navigationManager = car.GetCarService(CarContext.NavigationService)!.JavaCast<NavigationManager>()!;
                _navigationManager.SetNavigationManagerCallback(new NavigationCallback(this));
            }
        }

        Runtime.Navigation.AttachAsync(this).ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    return;
                }

                lock (_lock)
                {
                    if (_closed)
                    {
                        task.Result.Dispose();
                        return;
                    }

                    _attachment = task.Result;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            _attachment?.Dispose();
            _attachment = null;
        }

        _mapScreens.Clear();
        _surface?.Dispose();
        _surface = null;
        _navigationManager = null;
    }

    public void Dispose() => Close();

    public CarAppRendered RenderMap(CarMapTemplate map, CarAppScreenState screen)
    {
        if (!_mapScreens.Contains(screen))
        {
            _mapScreens.Add(screen);
        }

        return MapTemplateRenderer.Render(map, _context, screen, Runtime.Navigation.Snapshot, this);
    }

    public void Restore(CarNavigationSnapshot state)
    {
        UpdateNavigationManager(state, ended: false);
        Rerender();
    }

    public void Apply(CarNavigationChange change, CarNavigationSnapshot state)
    {
        switch (change)
        {
            case CarNavigationChange.AlertShown alert:
                ShowAlert(alert.Alert);
                return;
            case CarNavigationChange.Finished or CarNavigationChange.Cancelled or CarNavigationChange.EndedByCar:
                UpdateNavigationManager(state, ended: true, hostEnded: change is CarNavigationChange.EndedByCar);
                break;
            default:
                UpdateNavigationManager(state, ended: false);
                break;
        }

        Rerender();
    }

    public void OnPanModeChanged(bool panning) => Runtime.Surfaces.CarScreen.SetPanning(panning);

    public void OnRouteSelected(CarTrip trip, CarRouteChoice route) =>
        Runtime.Navigation.OnPreviewSelectedAsync(trip.Id, route.Id).Forget(Runtime.Errors, "Navigation.TripPreviewSelected");

    public void OnStartPressed()
    {
        var state = Runtime.Navigation.Snapshot;
        if (state.State != CarNavigationState.Previewing || (state.SelectedPreview ?? (state.Previews.IsEmpty ? null : state.Previews[0])) is not { } trip)
        {
            return;
        }

        var route = state.SelectedRoute is { } selected && trip.Routes.Any(r => r.Id == selected.Id) ? selected : trip.Routes[0];
        Runtime.Navigation.OnStartRequestedAsync(trip.Id, route.Id).Forget(Runtime.Errors, "Navigation.TripStartRequested");
    }

    // Map screens rebuilt with the current navigation state (NavigationTemplate: refresh; preview ↔ guidance: step)
    private void Rerender()
    {
        foreach (var screen in _mapScreens.ToArray())
        {
            screen.RequestRerender(_context.Post);
        }
    }

    // NavigationManager: navigationStarted once per guidance, updateTrip on every change, navigationEnded at the end
    private void UpdateNavigationManager(CarNavigationSnapshot state, bool ended, bool hostEnded = false)
    {
        if (_navigationManager is not { } manager)
        {
            return;
        }

        try
        {
            if (ended || state.State is CarNavigationState.Idle or CarNavigationState.Previewing)
            {
                if (_navigationStarted && !hostEnded)
                {
                    manager.NavigationEnded();
                }

                _navigationStarted = false;
                return;
            }

            if (!_navigationStarted)
            {
                manager.NavigationStarted();
                _navigationStarted = true;
            }

            manager.UpdateTrip(CarAppNavigationModels.Trip(state, CarAppIcons.For(_context, new CarAppScreenState()), LanesWarned, DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Runtime.Errors.HandleError(ex, new CarErrorContext("Navigation.NavigationManager", state.Trip));
        }
    }

    // Navigation alert: AppManager.ShowAlert (API 5, no callback as in Stage 2); dismissed after a timeout or an action
    private void ShowAlert(CarNavigationAlert alert)
    {
        if (_context.ApiLevel < CarNavigationTemplates.AlertApiLevel)
        {
            CarNavigationLog.AlertUnavailable(Runtime.Logger, alert.Id, CarNavigationTemplates.AlertApiLevel, _context.ApiLevel);
            return;
        }

        var id = ++_nextAlertId;
        var model = new CarAlertTemplate(alert.Title)
        {
            Id = alert.Id,
            Subtitle = alert.Subtitle,
            Icon = alert.Image,
            Actions = alert.SecondaryAction is { } secondary ? [alert.PrimaryAction, secondary] : [alert.PrimaryAction],
        };
        var native = AlertTemplateRenderer.Render(model, _context, id, alert.Duration, () =>
        {
            Dismiss(id);
            Runtime.Navigation.OnAlertDismissed(alert.Id);
            return Task.CompletedTask;
        });
        if (_carContext is { } car)
        {
            car.GetCarService(CarContext.AppService)!.JavaCast<AppManager>()!.ShowAlert(native);
        }

        Task.Delay(alert.Duration).ContinueWith(
            _ => _context.Post(() => Runtime.Navigation.OnAlertDismissed(alert.Id)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Dismiss(int id)
    {
        if (!_closed && _carContext is { } car)
        {
            car.GetCarService(CarContext.AppService)!.JavaCast<AppManager>()!.DismissAlert(id);
        }
    }

    // The host stopped navigation (onStopNavigation) or asks to simulate driving (onAutoDriveEnabled; DHU --autodrive)
    private sealed class NavigationCallback(CarAppNavigationConnection connection) : Java.Lang.Object, INavigationManagerCallback
    {
        public void OnStopNavigation()
        {
            connection._navigationStarted = false;
            connection.Runtime.Navigation.OnEndedByCarAsync().Forget(connection.Runtime.Errors, "Navigation.EndedByCar");
        }

        public void OnAutoDriveEnabled() =>
            connection.Runtime.Navigation.OnAutoDriveRequestedAsync().Forget(connection.Runtime.Errors, "Navigation.AutoDriveRequested");
    }
}
