using Android.Content;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using AndroidX.Lifecycle;
using CarKit.Maui.Renderers;
using NativeAction = AndroidX.Car.App.Model.Action;
using Screen = AndroidX.Car.App.Screen;

namespace CarKit.Maui.Navigation;

// Car App Library cluster session (spec: "Navigation module decisions (Stage 8)", Android hosting):
// SessionInfo.DISPLAY_TYPE_CLUSTER (Car App API 6, category FEATURE_CLUSTER), only a NavigationTemplate with the
// InstrumentCluster map, no driver actions (the cluster has no input). State from the same navigation session as the
// car screen. Main thread.
internal sealed class CarKitClusterSession(CarNavigationRuntime runtime) : Session
{
    private ClusterScreen? _screen;

    public override Screen OnCreateScreen(Intent? intent) => _screen = new ClusterScreen(CarContext!, runtime);

    // Cluster day/night: the InstrumentCluster surface and the template icons
    public override void OnCarConfigurationChanged(Android.Content.Res.Configuration? newConfiguration) => _screen?.OnContentStyleChanged();
}

internal sealed class ClusterScreen : Screen, ILifecycleEventObserver, ICarNavigationSink
{
    private readonly CarNavigationRuntime _runtime;
    private readonly CarImageLoader<CarIcon> _images;
    private readonly CarSurfaceHost? _surface;
    private readonly HashSet<string> _lanesWarned = [];
    // Attachment completes off the main thread, while ON_DESTROY arrives on the main one (review M11c)
    private readonly Lock _lock = new();
    private IDisposable? _attachment;
    private bool _destroyed;

    public ClusterScreen(CarContext carContext, CarNavigationRuntime runtime)
        : base(carContext)
    {
        _runtime = runtime;
        _images = new CarImageLoader<CarIcon>(new CarAppImages(carContext), runtime.Logger, combineDarkVariants: false);
        Lifecycle!.AddObserver(this);
        if (carContext.CarAppApiLevel >= CarNavigationTemplates.ClusterApiLevel)
        {
            _surface = new CarSurfaceHost(runtime.Surfaces.InstrumentCluster, runtime, carContext, carContext.IsDarkMode ? CarContentStyle.Dark : CarContentStyle.Light);
            carContext.GetCarService(CarContext.AppService)!.JavaCast<AppManager>()!.SetSurfaceCallback(new CarKitSurfaceCallback(_surface));
        }

        runtime.Navigation.AttachAsync(this).ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    return;
                }

                lock (_lock)
                {
                    if (!_destroyed)
                    {
                        _attachment = task.Result;
                        return;
                    }
                }

                task.Result.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public override ITemplate OnGetTemplate() => Template(_runtime.Navigation.Snapshot, new CarAppIcons(Icon, _runtime.Logger), _lanesWarned);

    // NavigationTemplate requires an ActionStrip; the cluster has no input, so only the app icon (also from device
    // tests)
    internal static ITemplate Template(CarNavigationSnapshot state, CarAppIcons icons, HashSet<string> lanesWarned)
    {
        var builder = new NavigationTemplate.Builder()
            .SetActionStrip(new ActionStrip.Builder().AddAction(NativeAction.AppIcon!)!.Build()!)!;
        MapTemplateRenderer.ApplyNavigationInfo(builder, state, icons, lanesWarned, DateTimeOffset.Now);
        return builder.Build()!;
    }

    public void OnContentStyleChanged()
    {
        if (_destroyed)
        {
            return;
        }

        _surface?.SetContentStyle(CarContext!.IsDarkMode ? CarContentStyle.Dark : CarContentStyle.Light);
        Refresh();
    }

    public void Apply(CarNavigationChange change, CarNavigationSnapshot state) => Refresh();

    public void Restore(CarNavigationSnapshot state) => Refresh();

    public void OnStateChanged(ILifecycleOwner source, Lifecycle.Event e)
    {
        if (!Equals(e, Lifecycle.Event.OnDestroy))
        {
            return;
        }

        IDisposable? attachment;
        lock (_lock)
        {
            _destroyed = true;
            attachment = _attachment;
            _attachment = null;
        }

        Lifecycle!.RemoveObserver(this);
        attachment?.Dispose();
        _surface?.Release();
        _images.Dispose();
    }

    private void Refresh()
    {
        if (!_destroyed)
        {
            Invalidate();
        }
    }

    // Icon available at once or after loading (cluster rebuild)
    private CarIcon? Icon(CarImage? image, int pixels)
    {
        var load = _images.Load(image, new CarImageRequest(pixels, CarContext!.IsDarkMode ? CarContentStyle.Dark : CarContentStyle.Light));
        if (load.Pending is { } pending)
        {
            pending.ContinueWith(
                _ => new Android.OS.Handler(Android.OS.Looper.MainLooper!).Post(Refresh),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        return load.Image;
    }
}
