using CarPlay;
using CoreFoundation;
using CoreGraphics;
using UIKit;

namespace CarKit.Maui.Renderers;

// What the CarPlay renderer needs from the host: the session (handlers on the main thread), images, the car screen
// scale, the theme and closing the presented modal before its action's handler (spec: "Template decisions (Stage 3)")
internal sealed class CarPlayRenderContext(
    CarSession session,
    ICarErrorHandler errors,
    CarImageLoader<UIImage> images,
    nfloat scale,
    Func<CarContentStyle> style,
    Func<string, Task> closeModal,
    IReadOnlyList<ICarPlayModuleRenderer>? modules = null,
    Func<CPTemplate, bool>? isShown = null)
{
    public CarSession Session { get; } = session;

    public ICarErrorHandler Errors { get; } = errors;

    public nfloat Scale { get; } = scale;

    public CarContentStyle Style => style();

    // Module renderers (Stage 5); null = a template without a module renderer
    public ICarPlayModuleRenderer? Module(CarTemplate template) => modules?.FirstOrDefault(m => m.CanRender(template));

    // Whether CarPlay shows the object (stack or modal) — shared templates (CPNowPlayingTemplate.SharedTemplate) can
    // appear only once
    public bool IsShown(CPTemplate template) => isShown?.Invoke(template) ?? false;

    // The handler of the current version of the element; the completion (CPListItem spinner) only after the task finishes
    public void Select(CarPlayTemplateState state, CarElement rendered, Action? completion = null)
    {
        var element = state.Current(rendered);
        Run(HandlerOf(element), Session.CreateContext(element), "Select", completion);
    }

    public void Run<TContext>(Func<TContext, Task>? handler, TContext context, string operation, Action? completion = null)
        where TContext : CarItemContext =>
        RunAsync(handler, context, operation, completion).Forget(Errors, operation);

    // An alert or sheet action: first close the modal and report to the session, then the handler (which may show
    // another modal)
    public void SelectModalAction(CarPlayTemplateState state, CarAction rendered) =>
        SelectModalActionAsync(state, state.Current(rendered)).Forget(Errors, "Select");

    // The image is available at once or null; an asynchronously loaded image goes to applyLater on the main thread if the
    // element (key) still awaits the same CarImage — also after in-place updates that did not change the image.
    // applyLater changes the native object (new CPContact, grid buttons, TabImage), so the template's ConfigureCarPlay
    // hooks from the state run again afterwards (spec: "Platform template decisions (Stage 4)")
    public UIImage? Image(CarPlayTemplateState state, string key, CarImage? image, CGSize maxSize, Action<UIImage> applyLater)
    {
        state.ExpectImage(key, image);
        if (image is null)
        {
            return null;
        }

        var maxPixels = (int)Math.Ceiling((double)(nfloat.Max(maxSize.Width, maxSize.Height) * Scale));
        var load = images.Load(image, new CarImageRequest(Math.Max(maxPixels, 1), Style));
        if (load.Pending is { } pending)
        {
            pending.ContinueWith(
                task =>
                {
                    if (task.Result is { } loaded)
                    {
                        DispatchQueue.MainQueue.DispatchAsync(() =>
                        {
                            if (state.Expects(key, image))
                            {
                                applyLater(loaded);
                                CarPlayHooks.Apply(state.Model, state.Native, this);
                            }
                        });
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        return load.Image;
    }

    public UIImage Placeholder(CGSize size) => CarPlayImages.Placeholder(size, Scale);

    private static Func<CarItemContext, Task>? HandlerOf(CarElement element) => element switch
    {
        CarListTemplateItem item => item.OnSelected,
        CarGridItem item => item.OnSelected,
        CarAction action => action.OnSelected,
        CarBarButton button => button.OnSelected,
        CarPlayContactButton button => button.OnSelected,
        _ => null,
    };

    private async Task RunAsync<TContext>(Func<TContext, Task>? handler, TContext context, string operation, Action? completion)
        where TContext : CarItemContext
    {
        try
        {
            await Session.InvokeAsync(handler, context, operation);
        }
        finally
        {
            completion?.Invoke();
        }
    }

    private async Task SelectModalActionAsync(CarPlayTemplateState state, CarAction action)
    {
        try
        {
            await closeModal(state.Model.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failed close must not block the action handler
            Errors.HandleError(ex, new CarErrorContext("Dismiss", action));
        }

        await Session.InvokeAsync(action.OnSelected, Session.CreateContext(action), "Select");
    }
}
