using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarKit.Maui.Testing;

/// <summary>The platform operation recorded by <see cref="FakeCarHost"/>.</summary>
public enum FakeCarHostOperation
{
    /// <summary>The root was replaced.</summary>
    SetRoot,

    /// <summary>A template was pushed.</summary>
    Push,

    /// <summary>The top template was popped.</summary>
    Pop,

    /// <summary>Templates were popped to a template.</summary>
    PopTo,

    /// <summary>Templates were popped to the root.</summary>
    PopToRoot,

    /// <summary>A modal template was presented.</summary>
    Present,

    /// <summary>The modal template was dismissed.</summary>
    Dismiss,

    /// <summary>A template was updated in place.</summary>
    Update,
}

/// <summary>A platform call recorded by <see cref="FakeCarHost"/>.</summary>
/// <param name="Operation">The operation.</param>
/// <param name="Template">The template sent to the platform, after validation, fallback and truncation.</param>
/// <param name="Animated">Whether the change was animated.</param>
/// <param name="OnMainThread">Whether the call ran on the (simulated) main thread.</param>
public sealed record FakeCarHostCall(FakeCarHostOperation Operation, CarTemplate? Template, bool Animated, bool OnMainThread);

/// <summary>
/// An in-memory car host for unit tests: connects an <see cref="ICarAppHandler"/>, records every platform
/// call and simulates what the driver does (select, back, search, disconnect).
/// </summary>
public sealed partial class FakeCarHost : ICarHost, ICarErrorHandler, IDisposable
{
    private readonly Lock _sync = new();
    private readonly List<FakeCarHostCall> _calls = [];
    // What the platform actually shows: templates after validation, fallback and truncation
    private readonly List<CarTemplate> _platformStack = [];
    private CarTemplate? _platformPresented;
    private readonly List<Exception> _errors = [];
    private readonly Dictionary<CarLimitKind, int> _limits = [];
    private Exception? _nextFailure;
    private CarSession? _session;
    private ICarAppHandler? _handler;
    private CarCapabilities? _capabilities;

    /// <summary>Creates the host.</summary>
    /// <param name="categories">The app categories.</param>
    /// <param name="platform">The simulated platform.</param>
    public FakeCarHost(CarAppCategory categories, CarPlatform platform = CarPlatform.CarPlay)
    {
        Options = new CarKitOptions { Categories = categories };
        Platform = platform;
        _navigation = new CarNavigationController(Dispatcher, this, () => NavigationHandler, NullLogger.Instance);
    }

    /// <summary>Gets the options; change them before <see cref="ConnectAsync(ICarAppHandler)"/>.</summary>
    public CarKitOptions Options { get; }

    /// <summary>Gets the simulated platform.</summary>
    public CarPlatform Platform { get; }

    /// <summary>Gets or sets the simulated iOS version (CarPlay).</summary>
    public Version IosVersion { get; set; } = new(26, 4);

    /// <summary>Gets or sets the simulated Car App API level (Android).</summary>
    public int CarAppApiLevel { get; set; } = 8;

    /// <summary>
    /// Gets the features the simulated CarKit build and car implement; <see cref="ICarCapabilities.Supports(CarFeature)"/>
    /// also needs the platform version from the rules. By default the newest CarKit build (iOS 26.4) in a car with video;
    /// remove a feature to simulate the <c>net10.0-ios</c> (iOS 26.0) build or a car without it. Change it before
    /// <see cref="ConnectAsync(ICarAppHandler)"/>.
    /// </summary>
    public ISet<CarFeature> Features { get; } = new HashSet<CarFeature>(CarFeatureDefaults.NewestAsset);

    /// <summary>Gets the current appearance.</summary>
    public CarContentStyle ContentStyle { get; private set; }

    /// <summary>Gets the dispatcher that simulates the main thread.</summary>
    public FakeCarDispatcher Dispatcher { get; } = new();

    /// <summary>Gets or sets the logger passed to the session.</summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>Gets or sets a delay added to every platform call, to simulate a slow platform.</summary>
    public TimeSpan CallDelay { get; set; }

    /// <summary>Gets the recorded platform calls.</summary>
    public IReadOnlyList<FakeCarHostCall> Calls
    {
        get
        {
            lock (_sync)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Gets the exceptions thrown by app callbacks.</summary>
    public IReadOnlyList<Exception> Errors
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    /// <summary>Gets the session created by <see cref="ConnectAsync(ICarAppHandler)"/>.</summary>
    public ICarSession? Session => _session;

    CarPlatform ICarHost.Platform => Platform;

    ICarCapabilities ICarHost.Capabilities => _capabilities ?? throw new InvalidOperationException("Call ConnectAsync first.");

    /// <summary>Sets a content limit the platform reports at runtime.</summary>
    /// <param name="kind">The limit.</param>
    /// <param name="maximum">The maximum.</param>
    public void SetLimit(CarLimitKind kind, int maximum) => _limits[kind] = maximum;

    /// <summary>Makes the next platform call fail with <paramref name="exception"/>.</summary>
    /// <param name="exception">The native error.</param>
    public void FailNextCall(Exception exception) => _nextFailure = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <summary>Connects the car and runs <see cref="ICarAppHandler.OnConnectedAsync"/> until it completes.</summary>
    /// <param name="handler">The app handler.</param>
    /// <returns>The session.</returns>
    public async Task<ICarSession> ConnectAsync(ICarAppHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _capabilities = new CarCapabilities(
            Platform,
            Platform == CarPlatform.CarPlay ? IosVersion : null,
            Platform == CarPlatform.CarPlay ? null : CarAppApiLevel,
            kind => _limits.TryGetValue(kind, out var maximum) ? maximum : null,
            Features.Contains);
        _session = new CarSession(this, Options, Dispatcher, this, Logger);
        // Stage 8: the car shows the current navigation state right after connecting (like CarPlay and Android Auto)
        await AttachNavigationAsync().ConfigureAwait(false);
        await _session.StartAsync(handler).ConfigureAwait(false);
        return _session;
    }

    /// <summary>Disconnects the car.</summary>
    /// <returns>A task that completes after <see cref="ICarAppHandler.OnDisconnectedAsync"/>.</returns>
    public Task DisconnectAsync()
    {
        var session = Connected();
        DetachNavigation();
        return session.StopAsync(_handler!);
    }

    /// <summary>Simulates the platform back button: the platform pops its top screen and tells the session.</summary>
    /// <returns>A task that completes when the session has synchronized its stack.</returns>
    public Task PressBackAsync()
    {
        var session = Connected();
        string popped;
        lock (_sync)
        {
            if (_platformStack.Count <= 1)
            {
                return Task.CompletedTask;
            }

            popped = _platformStack[^1].Id;
            _platformStack.RemoveAt(_platformStack.Count - 1);
        }

        return session.OnHostPoppedAsync(popped);
    }

    /// <summary>Simulates the platform dismissing the presented template, for example an alert timing out.</summary>
    /// <returns>A task that completes when the session has synchronized.</returns>
    public Task DismissPresentedAsync()
    {
        var session = Connected();
        string dismissed;
        lock (_sync)
        {
            if (_platformPresented is null)
            {
                return Task.CompletedTask;
            }

            dismissed = _platformPresented.Id;
            _platformPresented = null;
        }

        return session.OnHostDismissedAsync(dismissed);
    }

    /// <summary>Simulates a light or dark mode change.</summary>
    /// <param name="style">The new appearance.</param>
    /// <returns>A task that completes when the session has raised its event.</returns>
    public Task ChangeContentStyleAsync(CarContentStyle style)
    {
        var session = Connected();
        ContentStyle = style;
        return session.OnContentStyleChangedAsync(style);
    }

    /// <summary>
    /// Simulates the driver selecting a visible row, button, action or place. Selecting an action of the presented
    /// alert or action sheet first dismisses it, as both platforms do, and then runs the handler; an action of a voice
    /// control state keeps the template presented on CarPlay. On an Android Auto host
    /// a place without <see cref="CarPointOfInterest.OnSelected"/> runs its <see cref="CarPointOfInterest.PrimaryAction"/>,
    /// as the place list does.
    /// </summary>
    /// <param name="element">The element, matched by <see cref="CarElement.Id"/> in the top or presented template.</param>
    /// <returns>A task that completes when the selection handler completes.</returns>
    public async Task SelectAsync(CarElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var session = Connected();
        // Stage 8: a navigation alert action closes it before the handler (like CPNavigationAlert and the Android Auto
        // Alert)
        if (element is CarAction alertAction && NavigationAlertAction(alertAction) is { } navigationAction)
        {
            await session.InvokeAsync(navigationAction.OnSelected, session.CreateContext(navigationAction), "Select").ConfigureAwait(false);
            return;
        }

        var visible = FindVisible(element);
        if (visible is CarAction && DismissIfPresentedAction(visible) is { } dismissed)
        {
            await session.OnHostDismissedAsync(dismissed).ConfigureAwait(false);
        }

        // An Android Auto place row has no details card: without OnSelected the primary action runs (like the renderer)
        if (visible is CarPointOfInterest place && Platform != CarPlatform.CarPlay)
        {
            var (target, selection) = CarPointsOfInterest.AndroidAutoSelection(place);
            await session.InvokeAsync(selection, session.CreateContext(target), "Select").ConfigureAwait(false);
            return;
        }

        Func<CarItemContext, Task>? handler = visible switch
        {
            CarListTemplateItem item => item.OnSelected,
            CarGridItem item => item.OnSelected,
            CarAction action => action.OnSelected,
            CarBarButton button => button.OnSelected,
            CarMapButton button => button.OnSelected,
            CarPointOfInterest point => point.OnSelected,
            CarNowPlayingButton button => button.OnSelected,
            CarPlayContactButton button => button.OnSelected,
            _ => throw new InvalidOperationException($"{visible.GetType().Name} cannot be selected."),
        };
        await session.InvokeAsync(handler, session.CreateContext(visible), "Select").ConfigureAwait(false);
    }

    /// <summary>Simulates the driver flipping a row toggle.</summary>
    /// <param name="item">The row.</param>
    /// <param name="isChecked">The new state.</param>
    /// <returns>A task that completes when the toggle handler completes.</returns>
    public Task ToggleAsync(CarListItem item, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(item);
        var session = Connected();
        var visible = (CarListItem)FindVisible(item);
        return session.InvokeAsync(visible.Toggle?.OnToggled, new CarToggleContext(session, visible, isChecked, session.ConnectionToken), "Toggle");
    }

    /// <summary>Simulates the driver selecting one image of an image row.</summary>
    /// <param name="item">The image row.</param>
    /// <param name="index">The image index.</param>
    /// <returns>A task that completes when the handler completes.</returns>
    public Task SelectImageAsync(CarListImageRowItem item, int index)
    {
        ArgumentNullException.ThrowIfNull(item);
        var session = Connected();
        var visible = (CarListImageRowItem)FindVisible(item);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, visible.Images.Count);
        return session.InvokeAsync(visible.OnImageSelected, new CarImageRowContext(session, visible, index, session.ConnectionToken), "SelectImage");
    }

    /// <summary>Simulates the driver replying to a conversation by voice (Android Auto <c>onTextReply</c>).</summary>
    /// <param name="item">The conversation row, matched by <see cref="CarElement.Id"/> in the top template.</param>
    /// <param name="text">The reply text.</param>
    /// <returns>A task that completes when <see cref="CarConversationItem.OnReply"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The host is CarPlay, which hands conversations to Siri.</exception>
    public Task ReplyAsync(CarConversationItem item, string text)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);
        var session = Connected();
        var visible = Conversation(item);
        return session.InvokeAsync(visible.OnReply, new CarTextInputContext(session, visible, text, session.ConnectionToken), "Reply");
    }

    /// <summary>Simulates the car marking a conversation as read after reading it aloud (Android Auto <c>onMarkAsRead</c>).</summary>
    /// <param name="item">The conversation row, matched by <see cref="CarElement.Id"/> in the top template.</param>
    /// <returns>A task that completes when <see cref="CarConversationItem.OnMarkAsRead"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The host is CarPlay, which hands conversations to Siri.</exception>
    public Task MarkAsReadAsync(CarConversationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var session = Connected();
        var visible = Conversation(item);
        return session.InvokeAsync(visible.OnMarkAsRead, session.CreateContext(visible), "MarkAsRead");
    }

    // CarPlay hands the conversation to Siri (Intents Extension) and never calls OnReply/OnMarkAsRead
    private CarConversationItem Conversation(CarConversationItem item) =>
        Platform == CarPlatform.CarPlay
            ? throw new InvalidOperationException("CarPlay hands conversations to Siri and never calls OnReply or OnMarkAsRead; use an Android Auto host.")
            : FindVisible(item) as CarConversationItem
                ?? throw new InvalidOperationException($"Element '{item.Id}' is not a conversation row.");

    /// <summary>Simulates the driver panning or zooming the map of the point-of-interest template on top of the stack (CarPlay).</summary>
    /// <param name="region">The region the car now shows.</param>
    /// <returns>A task that completes when <see cref="CarPointOfInterestTemplate.OnMapRegionChanged"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The host is not CarPlay, or the top template is not a point-of-interest template.</exception>
    public Task ChangeMapRegionAsync(CarMapRegion region)
    {
        var session = Connected();
        if (Platform != CarPlatform.CarPlay)
        {
            throw new InvalidOperationException("Only CarPlay reports map region changes; Android Auto offers a refresh button instead (RefreshAsync).");
        }

        var map = Top<CarPointOfInterestTemplate>();
        return session.InvokeAsync(map.OnMapRegionChanged, new CarMapRegionContext(session, map, region, session.ConnectionToken), "MapRegionChanged");
    }

    /// <summary>Simulates the driver pressing refresh on the point-of-interest template on top of the stack (Android Auto).</summary>
    /// <returns>A task that completes when <see cref="CarPointOfInterestTemplate.OnRefreshRequested"/> completes.</returns>
    /// <exception cref="InvalidOperationException">
    /// The host is CarPlay, the app has no point-of-interest category (Android Auto then shows a list without a map and no
    /// refresh button), <see cref="CarAppApiLevel"/> is below 5, or the top template is not a point-of-interest template.
    /// </exception>
    public Task RefreshAsync()
    {
        var session = Connected();
        if (Platform == CarPlatform.CarPlay)
        {
            throw new InvalidOperationException("CarPlay has no refresh button; it reports map region changes instead (ChangeMapRegionAsync).");
        }

        // Like the renderer: the refresh button exists only on PlaceListMapTemplate (POI categories) from Car App API 5
        if (!CarPointsOfInterest.UsesPlaceListMap(Options.Categories))
        {
            throw new InvalidOperationException(
                $"Android Auto shows places of {Options.Categories} apps as a list without a map, which has no refresh button; only Parking, EV charging and fueling apps get one.");
        }

        if (CarAppApiLevel < CarPointsOfInterest.RefreshApiLevel)
        {
            throw new InvalidOperationException($"The refresh button needs Car App API level {CarPointsOfInterest.RefreshApiLevel}; the host has {CarAppApiLevel}.");
        }

        var map = Top<CarPointOfInterestTemplate>();
        return session.InvokeAsync(map.OnRefreshRequested, session.CreateContext(map), "RefreshRequested");
    }

    /// <summary>Simulates typing in the search template on top of the stack.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>The results returned by the app.</returns>
    public Task<IReadOnlyList<CarListItem>> SearchAsync(string text) => Connected().SearchAsync(Top<CarSearchTemplate>(), text);

    /// <summary>Simulates pressing search in the search template on top of the stack.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>A task that completes when the handler completes.</returns>
    public Task SubmitSearchAsync(string text)
    {
        var session = Connected();
        var search = Top<CarSearchTemplate>();
        return session.InvokeAsync(search.OnSearchSubmitted, new CarSearchContext(session, search, text, session.ConnectionToken), "SearchSubmitted");
    }

    /// <summary>Simulates selecting a tab of the tab template on top of the stack.</summary>
    /// <param name="tab">The tab, matched by <see cref="CarElement.Id"/>.</param>
    /// <returns>A task that completes when the handler completes.</returns>
    public Task SelectTabAsync(CarTemplate tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var session = Connected();
        var tabs = Top<CarTabTemplate>();
        var selected = tabs.Tabs.FirstOrDefault(t => t.Id == tab.Id)
            ?? throw new InvalidOperationException($"Tab '{tab.Id}' is not part of the tab template.");
        return session.InvokeAsync(tabs.OnTabSelected, new CarTabContext(session, tabs, selected, session.ConnectionToken), "TabSelected");
    }

    /// <summary>Simulates submitting text in an Android Auto input sign-in on top of the stack.</summary>
    /// <param name="text">The entered text.</param>
    /// <returns>A task that completes when the handler completes.</returns>
    public Task SubmitInputAsync(string text)
    {
        var session = Connected();
        var signIn = Top<AndroidAutoSignInTemplate>();
        var input = signIn.Method as AndroidAutoInputSignIn
            ?? throw new InvalidOperationException("The sign-in template does not use AndroidAutoInputSignIn.");
        return session.InvokeAsync(input.OnInputSubmitted, new CarTextInputContext(session, signIn, text, session.ConnectionToken), "InputSubmitted");
    }

    /// <summary>Releases the session and stops the dispatcher's main thread.</summary>
    public void Dispose()
    {
        DetachNavigation();
        _session?.Dispose();
        _navigation.Dispose();
        Dispatcher.Dispose();
    }

    Task ICarHost.SetRootAsync(CarTemplate root, bool animated) => RecordAsync(FakeCarHostOperation.SetRoot, root, animated, () =>
    {
        _platformStack.Clear();
        _platformStack.Add(root);
    });

    Task ICarHost.PushAsync(CarTemplate template, bool animated) =>
        RecordAsync(FakeCarHostOperation.Push, template, animated, () => _platformStack.Add(template));

    Task ICarHost.PopAsync(bool animated) =>
        RecordAsync(FakeCarHostOperation.Pop, null, animated, () => _platformStack.RemoveAt(_platformStack.Count - 1));

    Task ICarHost.PopToAsync(CarTemplate template, bool animated) => RecordAsync(FakeCarHostOperation.PopTo, template, animated, () =>
    {
        var index = _platformStack.FindIndex(t => t.Id == template.Id);
        _platformStack.RemoveRange(index + 1, _platformStack.Count - index - 1);
    });

    Task ICarHost.PopToRootAsync(bool animated) =>
        RecordAsync(FakeCarHostOperation.PopToRoot, null, animated, () => _platformStack.RemoveRange(1, _platformStack.Count - 1));

    Task ICarHost.PresentAsync(CarTemplate modal, bool animated) =>
        RecordAsync(FakeCarHostOperation.Present, modal, animated, () => _platformPresented = modal);

    Task ICarHost.DismissAsync(bool animated) =>
        RecordAsync(FakeCarHostOperation.Dismiss, null, animated, () => _platformPresented = null);

    Task ICarHost.UpdateAsync(CarTemplate previous, CarTemplate updated) => RecordAsync(FakeCarHostOperation.Update, updated, animated: false, () =>
    {
        var index = _platformStack.FindIndex(t => ReferenceEquals(t, previous));
        if (index >= 0)
        {
            _platformStack[index] = updated;
        }
        else if (ReferenceEquals(_platformPresented, previous))
        {
            _platformPresented = updated;
        }
    });

    TNative? ICarHost.GetNative<TNative>() where TNative : class => this as TNative;

    void ICarErrorHandler.HandleError(Exception exception, CarErrorContext context)
    {
        lock (_errors)
        {
            _errors.Add(exception);
        }
    }

    private async Task RecordAsync(FakeCarHostOperation operation, CarTemplate? template, bool animated, Action apply)
    {
        var onMainThread = !Dispatcher.IsDispatchRequired;
        if (CallDelay > TimeSpan.Zero)
        {
            await Task.Delay(CallDelay).ConfigureAwait(false);
        }

        if (Interlocked.Exchange(ref _nextFailure, null) is { } failure)
        {
            throw failure;
        }

        lock (_sync)
        {
            apply();
            _calls.Add(new FakeCarHostCall(operation, template, animated, onMainThread));
        }
    }

    private CarSession Connected() => _session ?? throw new InvalidOperationException("Call ConnectAsync first.");

    private TTemplate Top<TTemplate>() where TTemplate : CarTemplate
    {
        Connected();
        lock (_sync)
        {
            return _platformStack is [.., TTemplate top]
                ? top
                : throw new InvalidOperationException($"The top template on the platform is not {typeof(TTemplate).Name}.");
        }
    }

    // The action of a presented modal (alert, sheet or their fallback screen) closes it before the handler, like the
    // Stage 3 renderers
    private string? DismissIfPresentedAction(CarElement action)
    {
        lock (_sync)
        {
            // Stage 9: a voice control state action button (CPVoiceControlState) does not close the
            // CPVoiceControlTemplate
            if (_platformPresented is not { } presented || presented is CarVoiceControlTemplate
                || !CarModelWalker.Elements(presented).Any(e => ReferenceEquals(e, action)))
            {
                return null;
            }

            _platformPresented = null;
            return presented.Id;
        }
    }

    // Looks in what the platform shows (the presented modal, then the top of the stack); the same instance first
    private CarElement FindVisible(CarElement element)
    {
        Connected();
        CarTemplate?[] visible;
        lock (_sync)
        {
            visible = [_platformPresented, _platformStack.Count > 0 ? _platformStack[^1] : null];
        }

        foreach (var template in visible)
        {
            if (template is null)
            {
                continue;
            }

            var elements = CarModelWalker.Elements(template).ToList();
            if ((elements.Find(e => ReferenceEquals(e, element)) ?? elements.Find(e => e.Id == element.Id)) is { } match)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"{element.GetType().Name} '{element.Id}' is not visible on the platform.");
    }
}
