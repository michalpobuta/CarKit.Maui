using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Lifecycle;
using CarKit.Maui.Renderers;

namespace CarKit.Maui;

// A Car App Library screen with the current model; OnGetTemplate returns the template built by the renderer in the
// host operation, so a rendering error reaches the app's await, not the host callback. Marker = template Id (PopTo).
// The shape of the last template lets the CarKit host judge whether the next rebuild is a refresh (Stage 3).
// Stage 8: an obscured screen (ON_STOP) whose template type changed sends the last template first on return
// (CarAppScreenReturns), and the new one in the next frame.
internal sealed class CarKitScreen : Screen, ILifecycleEventObserver
{
    private readonly CarAppLibraryHost _host;
    private CarAppRendered _rendered;
    private CarAppRendered? _sent;
    private bool _returning;

    public CarKitScreen(CarContext carContext, CarAppLibraryHost host, CarTemplate? model, CarAppRendered rendered, CarAppScreenState state)
        : base(carContext)
    {
        _host = host;
        _rendered = rendered;
        Model = model;
        State = state;
        Marker = model?.Id;
        Lifecycle!.AddObserver(this);
    }

    public CarTemplate? Model { get; private set; }

    public string? TemplateId => Model?.Id;

    public CarAppScreenState State { get; }

    public bool IsModal => State.IsModal;

    public CarAppTemplateShape Shape => _rendered.Shape;

    // A screen removed by an app operation; its disappearance is not the user's "back"
    public bool RemovedByCarKit { get; set; }

    public void Show(CarTemplate model, CarAppRendered rendered)
    {
        Model = model;
        Marker = model.Id;
        _rendered = rendered;
        Invalidate();
    }

    public override ITemplate OnGetTemplate()
    {
        var returning = _returning;
        _returning = false;
        if (_sent is { } sent && CarAppScreenReturns.ResendLast(sent.Shape.Type, _rendered.Shape.Type, returning))
        {
            // A new template after the current response (Invalidate inside OnGetTemplate would be skipped by the host)
            _host.Events.Post(Invalidate);
            return sent.Template;
        }

        _sent = _rendered;
        return _rendered.Template;
    }

    public void OnStateChanged(ILifecycleOwner source, Lifecycle.Event e)
    {
        if (Equals(e, Lifecycle.Event.OnStop))
        {
            _returning = true;
        }
        else if (Equals(e, Lifecycle.Event.OnDestroy))
        {
            Lifecycle!.RemoveObserver(this);
            _host.OnScreenDestroyed(this);
        }
    }
}
