using CarKit.Maui.Renderers;
using CarPlay;
using CoreFoundation;
using Microsoft.Extensions.DependencyInjection;
using Foundation;
using UIKit;

namespace CarKit.Maui;

// ICarHost on CPInterfaceController. The session calls it on the main thread (contract in ICarHost).
// Native templates carry the model Id in UserInfo; after every operation the Id → CPTemplate map is rebuilt
// from CPInterfaceController.Templates, which is the source of truth about what CarPlay shows.
// The renderer state (model, native elements) per CPTemplate lets templates update in place (Stage 3).
internal sealed class CarPlayHost : ICarHost, IDisposable
{
    private CPInterfaceController? _controller;
    private CPTemplateApplicationScene? _scene;
    private readonly CarKitRuntime _runtime;
    private readonly CarNativeCalls _calls = new();
    private readonly CarPlayInterfaceDelegate _delegate;
    private readonly CarImageLoader<UIImage> _images;
    private readonly Dictionary<string, CPTemplate> _live = [];
    private readonly Dictionary<CPTemplate, CarPlayTemplateState> _states = new(ReferenceEqualityComparer.Instance);
    // Templates that CarPlay has already shown; the state disappears when such a template disappears (one that is
    // rendered but not yet sent stays)
    private readonly HashSet<CPTemplate> _shown = new(ReferenceEqualityComparer.Instance);
    // Templates that the current app operation removes; their disappearance is not the user's "back"
    private readonly HashSet<CPTemplate> _expectedRemovals = new(ReferenceEqualityComparer.Instance);
    private CarSession? _session;
    private CarPlayRenderContext? _context;
    private string? _presentedId;
    private bool _closed;
    private CPWindow? _window;
    private ICarPlayNavigationModule[] _navigationModules = [];
    // Stage 9: the asset's and the car's features (the CarFeature gate)
    private readonly CarPlayFeatures _features = new();

    public CarPlayHost(CPInterfaceController controller, CPTemplateApplicationScene scene, CarKitRuntime runtime, CPWindow? window = null)
    {
        _controller = controller;
        _scene = scene;
        _runtime = runtime;
        _window = window;
        var version = NSProcessInfo.ProcessInfo.OperatingSystemVersion;
        var limits = new CarRuntimeLimits(CarPlayLimits.Read, runtime.Logger);
        Capabilities = new CarCapabilities(CarPlatform.CarPlay, new Version((int)version.Major, (int)version.Minor), carAppApiLevel: null, limits.Get, _features.Supports);
        ContentStyle = ReadContentStyle(controller, scene);
        Scale = controller.CarTraitCollection.DisplayScale is var scale && scale > 0 ? scale : 2;
        _images = new CarImageLoader<UIImage>(new CarPlayImages(Scale), runtime.Logger, combineDarkVariants: true);
        _delegate = new CarPlayInterfaceDelegate(this);
        controller.Delegate = _delegate;
        Events = new CarHostEvents(work => DispatchQueue.MainQueue.DispatchAsync(work));
    }

    public CarPlatform Platform => CarPlatform.CarPlay;

    public ICarCapabilities Capabilities { get; }

    public CarContentStyle ContentStyle { get; private set; }

    // The car screen scale (images rendered in CarPlay pixels)
    public nfloat Scale { get; }

    // CarPlay events (back, dismiss, theme): the next turn of the main loop, dropped after Close()
    public CarHostEvents Events { get; }

    public void Attach(CarSession session)
    {
        _session = session;
        _context = new CarPlayRenderContext(
            session, _runtime.ErrorHandler, _images, Scale, () => ContentStyle, CloseModalAsync, [.. _runtime.Services.GetServices<ICarPlayModuleRenderer>()],
            template => _live.ContainsValue(template));
        // Stage 8: the Navigation module gets the window (map) and context before the app's OnConnectedAsync; a module
        // failure
        // does not abort the connection
        _navigationModules = [.. _runtime.Services.GetServices<ICarPlayNavigationModule>()];
        foreach (var module in _navigationModules)
        {
            RunModule(() => module.OnConnected(Context, _window), "OnConnected");
        }
    }

    // Background navigation (spec: "iOS hosting"): selecting a maneuver or alert notification → the Navigation module
    public void OnManeuverSelected(CPManeuver maneuver)
    {
        foreach (var module in _navigationModules)
        {
            RunModule(() => module.OnManeuverSelected(maneuver), "Navigation.ManeuverSelected");
        }
    }

    public void OnNavigationAlertSelected(CPNavigationAlert alert)
    {
        foreach (var module in _navigationModules)
        {
            RunModule(() => module.OnNavigationAlertSelected(alert), "Navigation.NavigationAlertSelected");
        }
    }

    // Disconnect: detach the delegate, finish pending calls, release references
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_context is { } context)
        {
            foreach (var module in _navigationModules)
            {
                RunModule(() => module.OnDisconnected(context), "OnDisconnected");
            }
        }

        _navigationModules = [];
        _window = null;
        Events.Close();
        _controller!.WeakDelegate = null;
        _calls.Close();
        _live.Clear();
        _states.Clear();
        _shown.Clear();
        _expectedRemovals.Clear();
        _session = null;
        _context = null;
        _images.Dispose();
        _delegate.Dispose();
        _features.Dispose();
        // Spec: on disconnect release the native objects; the session may live longer in the app
        _controller = null;
        _scene = null;
    }

    public void Dispose() => Close();

    public Task SetRootAsync(CarTemplate root, bool animated)
    {
        var native = Render(root);
        return RunAsync(Controller.Templates, () => Controller.SetRootTemplateAsync(native, animated));
    }

    public Task PushAsync(CarTemplate template, bool animated)
    {
        var native = Render(template);
        return RunAsync([], () => Controller.PushTemplateAsync(native, animated));
    }

    public Task PopAsync(bool animated) =>
        RunAsync(Controller.TopTemplate is { } top ? [top] : [], () => Controller.PopTemplateAsync(animated));

    public Task PopToAsync(CarTemplate template, bool animated)
    {
        var target = Native(template.Id);
        var above = Controller.Templates.SkipWhile(t => !ReferenceEquals(t, target)).Skip(1).ToArray();
        return RunAsync(above, () => Controller.PopToTemplateAsync(target, animated));
    }

    public Task PopToRootAsync(bool animated) =>
        RunAsync(Controller.Templates.Skip(1).ToArray(), () => Controller.PopToRootTemplateAsync(animated));

    public async Task PresentAsync(CarTemplate modal, bool animated)
    {
        var native = Render(modal);
        await RunAsync([], () => Controller.PresentTemplateAsync(native, animated));
        _presentedId = modal.Id;
        Sync();
    }

    public async Task DismissAsync(bool animated)
    {
        // The modal may have already closed the action choice (CloseModalAsync), so there is nothing to close natively
        if (Controller.PresentedTemplate is { } presented)
        {
            await RunAsync([presented], () => Controller.DismissTemplateAsync(animated));
        }

        _presentedId = null;
        Sync();
    }

    // Spec (Stage 3): in place where CarPlay has an API; replacement only at the top of the stack or for a modal
    public async Task UpdateAsync(CarTemplate previous, CarTemplate updated)
    {
        var native = Native(previous.Id);
        var state = _states[native];
        if (CarPlayTemplateRenderers.Update(state, updated, Context) == CarPlayUpdate.InPlace)
        {
            return;
        }

        if (previous.Id == _presentedId && ReferenceEquals(Controller.PresentedTemplate, native))
        {
            var replacement = Render(updated);
            await RunAsync([native], () => Controller.DismissTemplateAsync(false));
            await RunAsync([], () => Controller.PresentTemplateAsync(replacement, false));
            _presentedId = updated.Id;
            Sync();
        }
        else if (ReferenceEquals(Controller.TopTemplate, native))
        {
            var replacement = Render(updated);
            if (Controller.Templates.Length == 1)
            {
                await RunAsync([native], () => Controller.SetRootTemplateAsync(replacement, false));
            }
            else
            {
                await RunAsync([native], () => Controller.PopTemplateAsync(false));
                await RunAsync([], () => Controller.PushTemplateAsync(replacement, false));
            }
        }
        else
        {
            // Deeper in the stack CarPlay has no API for replacement: the rest in place, the list title/pane layout stay
            CarPlayTemplateRenderers.Update(state, updated, Context, keepFixed: true);
            CarKitRenderLog.FixedPropertyKept(_runtime.Logger, updated.Kind, updated.Id);
        }
    }

    public TNative? GetNative<TNative>() where TNative : class => _controller as TNative ?? _scene as TNative;

    public void OnContentStyleChanged(UIUserInterfaceStyle style)
    {
        var contentStyle = Map(style);
        ContentStyle = contentStyle;
        // Stage 8: the CarScreen map surface draws in the new style
        if (_context is { } context)
        {
            foreach (var module in _navigationModules)
            {
                RunModule(() => module.OnContentStyleChanged(context, contentStyle), "Navigation.ContentStyleChanged");
            }
        }

        // Spec: regenerating images with variants — an in-place update with the same model
        Events.Post(RefreshImages);
        Notify(session => session.OnContentStyleChangedAsync(contentStyle), "ContentStyleChanged");
    }

    // The CarPlay Back button or the modal closed by the system; reported without waiting (the session gate)
    public void OnTemplateDidDisappear(CPTemplate template)
    {
        if (_closed || _expectedRemovals.Remove(template) || template.UserInfo is not NSString idString)
        {
            return;
        }

        var id = idString.ToString();
        if (!_live.TryGetValue(id, out var live) || !ReferenceEquals(live, template))
        {
            return;
        }

        if (id == _presentedId)
        {
            if (Controller.PresentedTemplate is null)
            {
                _presentedId = null;
                _live.Remove(id);
                Notify(session => session.OnHostDismissedAsync(id), "HostDismissed");
            }

            return;
        }

        if (!Controller.Templates.Any(t => ReferenceEquals(t, template)))
        {
            _live.Remove(id);
            Notify(session => session.OnHostPoppedAsync(id), "HostPopped");
        }
    }

    private void RunModule(Action call, string operation)
    {
        try
        {
            call();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _runtime.ErrorHandler.HandleError(ex, new CarErrorContext(operation, null));
        }
    }

    private CarPlayRenderContext Context => _closed
        ? throw new CarNotConnectedException("CarPlay disconnected.")
        : _context ?? throw new InvalidOperationException("The CarPlay host is not attached to a session.");

    private CPInterfaceController Controller => _controller ?? throw new CarNotConnectedException("CarPlay disconnected.");

    private static CarContentStyle Map(UIUserInterfaceStyle style) => style == UIUserInterfaceStyle.Dark ? CarContentStyle.Dark : CarContentStyle.Light;

    private static CarContentStyle ReadContentStyle(CPInterfaceController controller, CPTemplateApplicationScene scene) =>
        Map(OperatingSystem.IsIOSVersionAtLeast(15, 4) ? scene.ContentStyle : controller.CarTraitCollection.UserInterfaceStyle);

    // Choosing an alert/sheet action (spec: "Template decisions (Stage 3)"): native close and report to the session before
    // the handler. Called from a CarPlay callback, not from an ICarHost call, so it may wait for the session gate.
    private async Task CloseModalAsync(string templateId)
    {
        if (_closed || _presentedId != templateId || Controller.PresentedTemplate is not { } presented)
        {
            return;
        }

        _presentedId = null;
        await RunAsync([presented], () => Controller.DismissTemplateAsync(true));
        if (_session is { } session)
        {
            await session.OnHostDismissedAsync(templateId);
        }
    }

    private void RefreshImages()
    {
        foreach (var state in _states.Values.ToArray())
        {
            try
            {
                CarPlayTemplateRenderers.Update(state, state.Model, Context, keepFixed: true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("ContentStyleChanged", state.Model));
            }
        }
    }

    // Report to the session without waiting (the session gate) and only on the next turn of the main loop
    private void Notify(Func<CarSession, Task> notify, string operation) => Events.Post(() =>
    {
        if (_session is { } session)
        {
            notify(session).Forget(_runtime.ErrorHandler, operation);
        }
    });

    private CPTemplate Render(CarTemplate template)
    {
        var state = CarPlayTemplateRenderers.Render(template, Context);
        _states[state.Native] = state;
        return state.Native;
    }

    private CPTemplate Native(string id) =>
        _live.TryGetValue(id, out var native) ? native : throw new InvalidOperationException($"CarPlay does not show a template with Id '{id}'.");

    private async Task RunAsync(CPTemplate[] removed, Func<Task<Tuple<bool, NSError>>> call)
    {
        foreach (var template in removed)
        {
            _expectedRemovals.Add(template);
        }

        try
        {
            // The async variant of CPInterfaceController returns (success, error) instead of throwing
            var (success, error) = await _calls.Run(call);
            if (error is not null)
            {
                throw new NSErrorException(error);
            }

            if (!success)
            {
                throw new InvalidOperationException("CarPlay did not complete the operation.");
            }
        }
        finally
        {
            foreach (var template in removed)
            {
                _expectedRemovals.Remove(template);
            }

            Sync();
        }
    }

    // CPInterfaceController.Templates and PresentedTemplate are the platform state after the operation; renderer states
    // only for them
    private void Sync()
    {
        if (_closed)
        {
            return;
        }

        _live.Clear();
        foreach (var template in Controller.Templates.Append(Controller.PresentedTemplate))
        {
            if (template?.UserInfo is NSString id)
            {
                _live[id.ToString()] = template;
                _shown.Add(template);
            }
        }

        foreach (var template in _shown.Where(t => !_live.ContainsValue(t)).ToArray())
        {
            _shown.Remove(template);
            _states.Remove(template);
            // The shared Now Playing is also pushed by CarPlay itself (the "Now Playing" button); without a model Id it
            // will not
            // reach the template map
            if (template is CPNowPlayingTemplate)
            {
                template.UserInfo = null;
            }
        }
    }
}
