using Android.Content;
using AndroidX.Car.App.Model;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Renderers;

// What the Car App Library renderer needs from the host: Car App API level, context (resources), session (handlers on
// the main thread), images and theme. Without CarContext, so the renderers can run on an emulator without a host
// (tests/CarKit.Maui.DeviceTests).
internal sealed class CarAppRenderContext(
    CarSession session,
    ICarErrorHandler errors,
    int apiLevel,
    Context context,
    CarImageLoader<CarIcon> images,
    Func<CarContentStyle> style,
    Action<System.Action> post,
    bool useSectionedItemTemplate,
    ILogger logger,
    IReadOnlyList<ICarAppModuleRenderer>? modules = null,
    bool usePlaceListMap = true)
{
    // Templates (Id + builder) for which the warning about non-matching hooks was already logged; main thread only
    private readonly HashSet<string> _unmatchedHooks = [];

    // POI templates for which the warning about the missing refresh button was already logged; main thread only
    private readonly HashSet<string> _unavailableRefresh = [];

    public CarSession Session { get; } = session;

    public ICarErrorHandler Errors { get; } = errors;

    public int ApiLevel { get; } = apiLevel;

    public Context Context { get; } = context;

    public bool UseSectionedItemTemplate { get; } = useSectionedItemTemplate;

    // Stage 7: POI as PlaceListMapTemplate (POI categories) or ListTemplate with places; fixed per connection
    public bool UsePlaceListMap { get; } = usePlaceListMap;

    public ILogger Logger { get; } = logger;

    // Module renderers (Stage 5); an empty list = no modules
    public IReadOnlyList<ICarAppModuleRenderer> Modules { get; } = modules ?? [];

    public ICarAppModuleRenderer? Module(CarTemplate template) => Modules.FirstOrDefault(m => m.CanRender(template, this));

    // Stage 8: the template type from a module (map: navigation state, POI: app map), otherwise the built-in one
    public string TemplateType(CarTemplate template) =>
        Module(template)?.TemplateType(template, this) ?? CarAppTemplateTypes.Of(template, UseSectionedItemTemplate, UsePlaceListMap);

    // The next turn of the main loop (rebuild after an image, search results, tab selection)
    public void Post(System.Action work) => post(work);

    // The template has Android Auto hooks, but none accepts the builder the host uses (e.g. a ListTemplate.Builder hook
    // on a host with SectionedItemTemplate): a warning once per template and builder in the connection, instead of a
    // silent skip
    public void ReportUnmatchedHooks(CarTemplate model, Type builder)
    {
        var name = builder.DeclaringType is { } owner ? owner.Name + "." + builder.Name : builder.Name;
        if (_unmatchedHooks.Add(model.Id + "|" + name))
        {
            CarKitRenderLog.AndroidAutoHooksNotApplied(Logger, model.Kind, model.Id, name);
        }
    }

    // OnRefreshRequested without a button in the car: a warning once per template in the connection, instead of a
    // silent skip
    public void ReportUnavailableRefresh(CarTemplate model, string reason)
    {
        if (_unavailableRefresh.Add(model.Id))
        {
            CarKitRenderLog.RefreshUnavailable(Logger, model.Kind, model.Id, reason);
        }
    }

    public IOnClickListener Click(CarElement element, Func<CarItemContext, Task>? handler) =>
        new ClickListener(() => Run(handler, Session.CreateContext(element), "Select"));

    // A modal action (alert, fallback screen): first close and report to the session, then the handler. The close is read
    // at click time: the host binds it to the screen state only after the screen exists (the first render happens earlier)
    public IOnClickListener ModalAction(Func<Func<Task>?> close, CarAction action) =>
        new ClickListener(() => ModalActionAsync(close(), action).Forget(Errors, "Select"));

    public void Run<TContext>(Func<TContext, Task>? handler, TContext context, string operation)
        where TContext : CarItemContext =>
        Session.InvokeAsync(handler, context, operation).Forget(Errors, operation);

    // The image is available at once, or a "loading" state; after loading the screen is rebuilt (a refresh)
    public CarImageLoad<CarIcon> Load(CarAppScreenState screen, CarImage? image, int maxPixels)
    {
        var load = images.Load(image, new CarImageRequest(maxPixels, style()));
        if (load.Pending is { } pending)
        {
            pending.ContinueWith(
                _ => screen.RequestRerender(post),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        return load;
    }

    public CarIcon? Icon(CarAppScreenState screen, CarImage? image, int maxPixels) => Load(screen, image, maxPixels).Image;

    private async Task ModalActionAsync(Func<Task>? close, CarAction action)
    {
        if (close is not null)
        {
            try
            {
                await close();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Errors.HandleError(ex, new CarErrorContext("Dismiss", action));
            }
        }

        await Session.InvokeAsync(action.OnSelected, Session.CreateContext(action), "Select");
    }

    private sealed class ClickListener(System.Action onClick) : Java.Lang.Object, IOnClickListener
    {
        public void OnClick() => onClick();
    }
}
