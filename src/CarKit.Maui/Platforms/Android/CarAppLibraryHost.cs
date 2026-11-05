using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// ICarHost on ScreenManager (spec: "Stack mapping", "Template decisions (Stage 3)"). The session calls it on the main
// thread (contract in ICarHost). _screens mirrors the ScreenManager stack; a screen carries the model, Id (Marker),
// state and template shape. Every rebuild is classified by the Car App Library refresh rules; CarAppTaskSteps counts
// the task steps.
internal sealed class CarAppLibraryHost : ICarHost, IDisposable
{
    // Spec: Alert via AppManager.ShowAlert from Car App API 5; the duration when the model does not provide one
    private const int AlertApiLevel = 5;
    private static readonly TimeSpan _defaultAlertDuration = TimeSpan.FromSeconds(10);

    private CarContext? _carContext;
    private readonly CarKitRuntime _runtime;
    private readonly Handler _mainLooper;
    private readonly List<CarKitScreen> _screens = [];
    private readonly CarAppTaskSteps _steps = new();
    private readonly CarImageLoader<CarIcon> _images;
    private CarSession? _session;
    private CarAppRenderContext? _context;
    private CarKitScreen? _modalScreen;
    private PresentedAlert? _alert;
    private readonly bool _useSectionedItemTemplate;
    private readonly bool _usePlaceListMap;
    private int _nextAlertId;
    private bool _closed;

    public CarAppLibraryHost(CarContext carContext, CarKitRuntime runtime)
    {
        _carContext = carContext;
        _runtime = runtime;
        _mainLooper = new Handler(Looper.MainLooper!);
        Events = new CarHostEvents(work => _mainLooper.Post(work));
        Platform = carContext.PackageManager?.HasSystemFeature(PackageManager.FeatureAutomotive) == true
            ? CarPlatform.AndroidAutomotive
            : CarPlatform.AndroidAuto;
        var limits = new CarRuntimeLimits(kind => CarAppLimits.Read(_carContext, kind), runtime.Logger);
        Capabilities = new CarCapabilities(Platform, iosVersion: null, carContext.CarAppApiLevel, limits.Get);
        ContentStyle = ReadContentStyle();
        _images = new CarImageLoader<CarIcon>(new CarAppImages(carContext), runtime.Logger, combineDarkVariants: false);
        // The List/Sectioned choice is fixed per connection (the same type for the host on every rebuild,
        // including the loading screen); Communication → ListTemplate (ConversationItem, Stage 6)
        _useSectionedItemTemplate = UsesSectionedItemTemplate(carContext.CarAppApiLevel, runtime.Options);
        // Stage 7: PlaceListMapTemplate is allowed only for apps with a POI category; others get a list of places
        _usePlaceListMap = CarRenderSwitches.UsePlaceListMap(runtime.Options.Categories);
    }

    // The connection's list type from the host API level, the AppContext switch and the app categories
    // (device test without CarContext)
    internal static bool UsesSectionedItemTemplate(int apiLevel, CarKitOptions options) =>
        CarRenderSwitches.UseSectionedItemTemplate(
            apiLevel,
            AppContext.TryGetSwitch(CarRenderSwitches.SectionedItemTemplate, out var enabled) ? enabled : null,
            options.Categories);

    public CarPlatform Platform { get; }

    public ICarCapabilities Capabilities { get; }

    public CarContentStyle ContentStyle { get; private set; }

    // Host events and the connection start: next turn of the main loop, dropped after Close()
    public CarHostEvents Events { get; }

    private CarContext CarContext => _carContext ?? throw new CarNotConnectedException("The car disconnected.");

    private int ApiLevel => CarContext.CarAppApiLevel;

    private CarAppRenderContext Context => _context ?? throw new InvalidOperationException("The car app host is not attached to a session.");

    private ScreenManager Screens => _closed ? throw new CarNotConnectedException("The car disconnected.") : _screens[0].ScreenManager!;

    // The main screen shows loading until OnConnectedAsync sets the root
    public CarKitScreen CreateRootScreen()
    {
        var root = NewScreen(model: null, CarAppTemplateRenderers.Loading(CarContext, ApiLevel, _useSectionedItemTemplate), new CarAppScreenState { IsRoot = true });
        _screens.Add(root);
        _steps.ResetTo(root, root.Shape.Type);
        return root;
    }

    public void Attach(CarSession session)
    {
        _session = session;
        _context = new CarAppRenderContext(
            session, _runtime.ErrorHandler, ApiLevel, CarContext, _images, () => ContentStyle, work => Events.Post(work), _useSectionedItemTemplate, _runtime.Logger,
            [.. _runtime.Services.GetServices<ICarAppModuleRenderer>()], _usePlaceListMap);
        // Modules get the connection (e.g. the media session token for MediaPlaybackTemplate); a module failure does not
        // abort the connection
        foreach (var module in _context.Modules)
        {
            try
            {
                module.OnConnected(_context, CarContext);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("OnConnected", null));
            }
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        // Stage 8: modules release connection resources (map surface, NavigationManager) before CarContext is released
        if (_context is { } context)
        {
            foreach (var module in context.Modules)
            {
                try
                {
                    module.OnDisconnected(context);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("OnDisconnected", null));
                }
            }
        }

        Events.Close();
        _alert?.Expiry.Cancel();
        _alert?.Expiry.Dispose();
        _alert = null;
        _modalScreen = null;
        foreach (var screen in _screens)
        {
            screen.State.Rerender = null;
        }

        _screens.Clear();
        _images.Dispose();
        _session = null;
        _context = null;
        // Spec: on disconnect release the references to CarContext (Java peer); the session may live longer in the app
        _carContext = null;
    }

    public void Dispose() => Close();

    // The same template type → PopToRoot and rebuild the root; a different type → a new root screen, because the host
    // treats returning to a screen with a different template type as an error. A modal screen stays above the root (like
    // a CarPlay modal), and with a new root screen it is closed together with the rest of the stack.
    public Task SetRootAsync(CarTemplate root, bool animated)
    {
        var current = _screens[0];
        var type = Context.TemplateType(root);
        if (CarAppRootPlans.For(current.Shape.Type, type) == CarAppRootPlan.ReplaceRoot)
        {
            var replacement = NewScreen(root, state: new CarAppScreenState { IsRoot = true });
            if (_modalScreen is { TemplateId: { } modalId })
            {
                _session?.OnHostClosedModalDuringOperation(modalId);
            }

            Screens.Push(replacement);
            foreach (var old in _screens)
            {
                old.RemovedByCarKit = true;
                Screens.Remove(old);
            }

            _screens.Clear();
            _screens.Add(replacement);
            _modalScreen = null;
            _steps.ResetTo(replacement, replacement.Shape.Type);
            return Task.CompletedTask;
        }

        // The root state (tab, search) belongs to the previous model; a different root starts over
        if (current.TemplateId != root.Id)
        {
            current.State.Reset();
        }

        // After PopToRoot the root's steps remain; a new template that is not a refresh adds a step (checked before the
        // stack changes)
        var rendered = CarAppTemplateRenderers.Render(root, Context, current.State);
        if (!CarAppRefreshRules.IsRefresh(current.Shape, rendered.Shape))
        {
            CheckSteps(CarAppTaskSteps.Cost(rendered.Shape.Type), rendered.Shape.Type, baseline: _steps.StepsOf(current));
        }

        if (_modalScreen is not null)
        {
            RemoveBelowModal(1);
        }
        else if (_screens.Count > 1)
        {
            MarkRemoved(1);
            Screens.PopToRoot();
            _screens.RemoveRange(1, _screens.Count - 1);
        }

        Show(current, root, rendered);
        return Task.CompletedTask;
    }

    // A screen cannot be pushed under a modal screen, so a push closes the modal (the session learns at once, without
    // the gate)
    public Task PushAsync(CarTemplate template, bool animated)
    {
        var type = Context.TemplateType(template);
        CheckSteps(CarAppTaskSteps.Cost(type), type, baseline: _steps.Total - (_modalScreen is { } open ? _steps.StepsOf(open) : 0));
        if (_modalScreen is { } modal)
        {
            RemoveModalScreen(modal);
            if (modal.TemplateId is { } modalId)
            {
                _session?.OnHostClosedModalDuringOperation(modalId);
            }
        }

        var screen = NewScreen(template, state: new CarAppScreenState());
        Screens.Push(screen);
        _screens.Add(screen);
        _steps.Pushed(screen, type);
        return Task.CompletedTask;
    }

    // The modal screen stays on top (like a CarPlay modal above the stack); the screens beneath it are removed
    public Task PopAsync(bool animated)
    {
        if (_modalScreen is not null)
        {
            RemoveBelowModal(_screens.Count - 2);
            return Task.CompletedTask;
        }

        MarkRemoved(_screens.Count - 1);
        Screens.Pop();
        _screens.RemoveAt(_screens.Count - 1);
        return Task.CompletedTask;
    }

    public Task PopToAsync(CarTemplate template, bool animated)
    {
        var index = _screens.FindIndex(s => s.TemplateId == template.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"No screen shows a template with Id '{template.Id}'.");
        }

        if (_modalScreen is not null)
        {
            RemoveBelowModal(index + 1);
            return Task.CompletedTask;
        }

        MarkRemoved(index + 1);
        Screens.PopTo(template.Id);
        _screens.RemoveRange(index + 1, _screens.Count - index - 1);
        return Task.CompletedTask;
    }

    public Task PopToRootAsync(bool animated)
    {
        if (_modalScreen is not null)
        {
            RemoveBelowModal(1);
            return Task.CompletedTask;
        }

        MarkRemoved(1);
        Screens.PopToRoot();
        _screens.RemoveRange(1, _screens.Count - 1);
        return Task.CompletedTask;
    }

    public Task PresentAsync(CarTemplate modal, bool animated)
    {
        if (modal is CarAlertTemplate alert && ApiLevel >= AlertApiLevel)
        {
            ShowAlert(alert, ++_nextAlertId);
            return Task.CompletedTask;
        }

        // A modal without a native counterpart (validator fallback, e.g. Message) is a screen on the stack and consumes
        // a step
        var modalType = Context.TemplateType(modal);
        CheckSteps(CarAppTaskSteps.Cost(modalType), modalType);
        var state = new CarAppScreenState { IsModal = true };
        var screen = NewScreen(modal, state: state);
        state.CloseModal = () => CloseModalScreenAsync(screen);
        Screens.Push(screen);
        _screens.Add(screen);
        _steps.Pushed(screen, modalType);
        _modalScreen = screen;
        return Task.CompletedTask;
    }

    public Task DismissAsync(bool animated)
    {
        if (_alert is { } alert)
        {
            ClearAlert();
            AppManager.DismissAlert(alert.Id);
        }
        else if (_modalScreen is { } screen)
        {
            RemoveModalScreen(screen);
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(CarTemplate previous, CarTemplate updated)
    {
        if (_alert is { } alert && alert.TemplateId == previous.Id && updated is CarAlertTemplate updatedAlert)
        {
            // The same alert identifier: the host replaces the alert being shown
            ClearAlert();
            ShowAlert(updatedAlert, alert.Id);
            return Task.CompletedTask;
        }

        var screen = _screens.Find(s => s.TemplateId == previous.Id)
            ?? throw new InvalidOperationException($"No screen shows a template with Id '{previous.Id}'.");
        var rendered = CarAppTemplateRenderers.Render(updated, Context, screen.State);
        if (!CarAppRefreshRules.IsRefresh(screen.Shape, rendered.Shape))
        {
            CheckSteps(CarAppTaskSteps.Cost(rendered.Shape.Type), rendered.Shape.Type);
        }

        Show(screen, updated, rendered);
        return Task.CompletedTask;
    }

    public TNative? GetNative<TNative>() where TNative : class => _carContext as TNative;

    // Stage 8: session intents (OnCreateScreen, OnNewIntent) for modules, e.g. ACTION_NAVIGATE for Navigation
    public void OnIntent(Android.Content.Intent intent)
    {
        if (_closed || _context is not { } context)
        {
            return;
        }

        foreach (var module in context.Modules)
        {
            try
            {
                module.OnIntent(context, intent);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("Intent", null));
            }
        }
    }

    public void OnCarConfigurationChanged()
    {
        if (_closed || ReadContentStyle() is var style && style == ContentStyle)
        {
            return;
        }

        ContentStyle = style;
        if (_context is { } context)
        {
            foreach (var module in context.Modules)
            {
                try
                {
                    module.OnContentStyleChanged(context, style);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("ContentStyleChanged", null));
                }
            }
        }

        // Images with a dark variant: rebuild the screens (the same model → refresh)
        Events.Post(() => _screens.ToList().ForEach(Rerender));
        Notify(session => session.OnContentStyleChangedAsync(style), "ContentStyleChanged");
    }

    // The host's back removes the screen by itself. On disconnect ScreenManager destroys the screens before the
    // session's ON_DESTROY, so the report goes through Events (deferred, dropped after Close()); otherwise the disconnect
    // would eat the session's stack
    public void OnScreenDestroyed(CarKitScreen screen)
    {
        if (_closed || screen.RemovedByCarKit || !_screens.Remove(screen) || screen.TemplateId is not { } id)
        {
            return;
        }

        _steps.Removed(screen);
        if (ReferenceEquals(screen, _modalScreen))
        {
            _modalScreen = null;
            Notify(session => session.OnHostDismissedAsync(id), "HostDismissed");
        }
        else
        {
            Notify(session => session.OnHostPoppedAsync(id), "HostPopped");
        }
    }

    private AppManager AppManager => CarContext.GetCarService(AndroidX.Car.App.CarContext.AppService)!.JavaCast<AppManager>();

    private CarContentStyle ReadContentStyle() => CarContext.IsDarkMode ? CarContentStyle.Dark : CarContentStyle.Light;

    private CarKitScreen NewScreen(CarTemplate? model, CarAppRendered? rendered = null, CarAppScreenState? state = null)
    {
        state ??= new CarAppScreenState();
        rendered ??= CarAppTemplateRenderers.Render(model!, Context, state);
        var screen = new CarKitScreen(CarContext, this, model, rendered, state);
        state.Rerender = () => Rerender(screen);
        return screen;
    }

    private void Show(CarKitScreen screen, CarTemplate model, CarAppRendered rendered)
    {
        var isRefresh = CarAppRefreshRules.IsRefresh(screen.Shape, rendered.Shape);
        screen.Show(model, rendered);
        _steps.Updated(screen, isRefresh, rendered.Shape.Type);
    }

    // Rebuild with the current model (image loaded, search results, tab selection, theme), outside an ICarHost call
    private void Rerender(CarKitScreen screen)
    {
        if (_closed || screen.Model is not { } model || !_screens.Contains(screen))
        {
            return;
        }

        try
        {
            Show(screen, model, CarAppTemplateRenderers.Render(model, Context, screen.State));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("Rerender", model));
        }
    }

    // The host shows at most 5 templates per task, and the fifth must end the task (Pane, Message, LongMessage, SignIn);
    // exceeding the limit follows the validation mode (spec: "Template decisions (Stage 3)"); LongMessage and SignIn
    // cost 0 (Stage 4).
    // baseline: the steps that remain after the operation, before the new one is added (SetRootAsync: only the root's steps)
    private void CheckSteps(int additional, string templateType, int? baseline = null)
    {
        var actual = (baseline ?? _steps.Total) + additional;
        if (CarAppTaskSteps.Allows(actual, templateType) || _runtime.Options.Validation == CarValidationMode.Off)
        {
            return;
        }

        var exception = new CarTemplateLimitException(
            $"Android Auto shows at most {CarAppTaskSteps.HostLimit} templates per task and the last one must end the task "
            + $"(PaneTemplate, MessageTemplate, LongMessageTemplate, SignInTemplate); this change would send {templateType} as template {actual}. "
            + "Updates that change a template's title, rows or sections count as new templates; pop screens, keep the structure "
            + "or end the task with an information or message template.",
            CarLimitKind.StackDepth,
            CarAppTaskSteps.HostLimit,
            actual);
        if (_runtime.Options.Validation == CarValidationMode.Strict)
        {
            throw exception;
        }

        CarKitLog.ValidationWarning(_runtime.Logger, exception.Message, exception);
    }

    // Report to the session without waiting (the session gate) and only on the next turn of the main loop
    private void Notify(Func<CarSession, Task> notify, string operation) => Events.Post(() =>
    {
        if (_session is { } session)
        {
            notify(session).Forget(_runtime.ErrorHandler, operation);
        }
    });

    private void MarkRemoved(int fromIndex)
    {
        for (var i = fromIndex; i < _screens.Count; i++)
        {
            _screens[i].RemovedByCarKit = true;
            _steps.Removed(_screens[i]);
            if (ReferenceEquals(_screens[i], _modalScreen))
            {
                _modalScreen = null;
            }
        }
    }

    // Screens from fromIndex up to the screen under the modal (the modal is always on top); invisible, so no return step
    private void RemoveBelowModal(int fromIndex)
    {
        for (var i = _screens.Count - 2; i >= fromIndex; i--)
        {
            var screen = _screens[i];
            screen.RemovedByCarKit = true;
            Screens.Remove(screen);
            _screens.RemoveAt(i);
            _steps.Removed(screen);
        }
    }

    private void RemoveModalScreen(CarKitScreen screen)
    {
        _modalScreen = null;
        screen.RemovedByCarKit = true;
        Screens.Remove(screen);
        _screens.Remove(screen);
        _steps.Removed(screen);
    }

    // A modal screen's action (e.g. ActionSheet → Message): close it and report to the session before the action handler
    private async Task CloseModalScreenAsync(CarKitScreen screen)
    {
        if (_closed || !ReferenceEquals(screen, _modalScreen) || screen.TemplateId is not { } id)
        {
            return;
        }

        RemoveModalScreen(screen);
        if (_session is { } session)
        {
            await session.OnHostDismissedAsync(id);
        }
    }

    // Binding 1.7.0.1 does not allow setting AlertCallback (spec: Risks), so CarKit assumes the host closed the alert
    // after the alert's duration or after an action click
    private void ShowAlert(CarAlertTemplate model, int id)
    {
        var duration = model.Duration is { } value && value > TimeSpan.Zero ? value : _defaultAlertDuration;
        var expiry = new CancellationTokenSource();
        _alert = new PresentedAlert(id, model.Id, expiry);
        AppManager.ShowAlert(AlertTemplateRenderer.Render(model, Context, id, duration, () => CloseAlertAsync(id)));
        ExpireAlertAsync(id, duration, expiry.Token).Forget(_runtime.ErrorHandler, "AlertExpired");
    }

    private async Task ExpireAlertAsync(int id, TimeSpan duration, CancellationToken token)
    {
        try
        {
            await Task.Delay(duration, token);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        await _runtime.Dispatcher.DispatchAsync(() =>
        {
            OnAlertClosedByHost(id);
            return Task.CompletedTask;
        });
    }

    // Alert action selection: CarKit closes the alert and reports to the session before the action handler runs
    // (the next modal is possible at once)
    private async Task CloseAlertAsync(int id)
    {
        if (_closed || _alert is not { } alert || alert.Id != id)
        {
            return;
        }

        ClearAlert();
        AppManager.DismissAlert(id);
        if (_session is { } session)
        {
            await session.OnHostDismissedAsync(alert.TemplateId);
        }
    }

    private void OnAlertClosedByHost(int id)
    {
        if (_closed || _alert is not { } alert || alert.Id != id)
        {
            return;
        }

        ClearAlert();
        Notify(session => session.OnHostDismissedAsync(alert.TemplateId), "HostDismissed");
    }

    private void ClearAlert()
    {
        _alert?.Expiry.Cancel();
        _alert?.Expiry.Dispose();
        _alert = null;
    }

    private sealed record PresentedAlert(int Id, string TemplateId, CancellationTokenSource Expiry);
}
