namespace CarKit.Maui;

/// <summary>The car platform a session runs on.</summary>
public enum CarPlatform
{
    /// <summary>Apple CarPlay.</summary>
    CarPlay,

    /// <summary>Android Auto (projected from the phone).</summary>
    AndroidAuto,

    /// <summary>Android Automotive OS running the Car App Library host.</summary>
    AndroidAutomotive,
}

/// <summary>The car display appearance.</summary>
public enum CarContentStyle
{
    /// <summary>Light appearance.</summary>
    Light,

    /// <summary>Dark appearance.</summary>
    Dark,
}

/// <summary>A limit checked by the validator before a template reaches the platform.</summary>
public enum CarLimitKind
{
    /// <summary>Rows in a list, across all sections.</summary>
    ListItems,

    /// <summary>Sections in a list.</summary>
    ListSections,

    /// <summary>Buttons in a grid.</summary>
    GridItems,

    /// <summary>Places in a point-of-interest template.</summary>
    PointsOfInterest,

    /// <summary>Label and value pairs in an information template.</summary>
    InformationItems,

    /// <summary>Tabs in a tab template.</summary>
    Tabs,

    /// <summary>
    /// Actions in a template; the limit comes from the platform rules per template. On CarPlay,
    /// <see cref="ICarCapabilities.GetLimit"/> returns the limit of alert actions, which depends on the app's entitlement
    /// (one action for audio apps).
    /// </summary>
    Actions,

    /// <summary>Templates on the navigation stack, root included.</summary>
    StackDepth,

    /// <summary>Buttons over the map of a <see cref="CarMapTemplate"/>.</summary>
    MapButtons,
}

/// <summary>Implemented by the app; receives a session each time a car connects.</summary>
public interface ICarAppHandler
{
    /// <summary>Called when a car connects. Set the root template here.</summary>
    /// <param name="session">The new session.</param>
    /// <param name="ct">Cancelled when the car disconnects.</param>
    /// <returns>A task that completes when the handler finishes.</returns>
    Task OnConnectedAsync(ICarSession session, CancellationToken ct);

    /// <summary>Called after the car disconnects.</summary>
    /// <param name="session">The disconnected session.</param>
    /// <returns>A task that completes when the handler finishes.</returns>
    Task OnDisconnectedAsync(ICarSession session) => Task.CompletedTask;
}

/// <summary>A connection to one car screen, with an imperative navigation stack.</summary>
public interface ICarSession
{
    /// <summary>Gets the platform.</summary>
    CarPlatform Platform { get; }

    /// <summary>Gets what the connected platform and version support.</summary>
    ICarCapabilities Capabilities { get; }

    /// <summary>Gets the current appearance.</summary>
    CarContentStyle ContentStyle { get; }

    /// <summary>Gets the navigation stack, root first. It stays readable after a disconnect.</summary>
    IReadOnlyList<CarTemplate> Stack { get; }

    /// <summary>Gets the presented modal template.</summary>
    CarTemplate? Presented { get; }

    /// <summary>Gets a value indicating whether the car is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Replaces the whole stack with <paramref name="root"/>.</summary>
    /// <param name="root">The root template.</param>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform shows the template.</returns>
    Task SetRootAsync(CarTemplate root, bool animated = false);

    /// <summary>Pushes a template.</summary>
    /// <param name="template">The template.</param>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform shows the template.</returns>
    Task PushAsync(CarTemplate template, bool animated = true);

    /// <summary>Pops the top template.</summary>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform removes the template.</returns>
    Task PopAsync(bool animated = true);

    /// <summary>Pops templates above <paramref name="template"/>.</summary>
    /// <param name="template">A template on the stack, matched by <see cref="CarElement.Id"/>.</param>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform removes the templates.</returns>
    Task PopToAsync(CarTemplate template, bool animated = true);

    /// <summary>Pops all templates above the root.</summary>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform removes the templates.</returns>
    Task PopToRootAsync(bool animated = true);

    /// <summary>Presents an alert, action sheet or voice control template.</summary>
    /// <param name="modal">The modal template.</param>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform shows the template.</returns>
    Task PresentAsync(CarModalTemplate modal, bool animated = true);

    /// <summary>Dismisses the presented template; does nothing when none is presented.</summary>
    /// <param name="animated">Whether to animate.</param>
    /// <returns>A task that completes when the platform dismisses the template.</returns>
    Task DismissAsync(bool animated = true);

    /// <summary>Replaces a template on the stack or the presented one with a new version that has the same <see cref="CarElement.Id"/> and type.</summary>
    /// <param name="updated">The new version.</param>
    /// <returns>A task that completes when the platform shows the change.</returns>
    Task UpdateAsync(CarTemplate updated);

    /// <summary>Raised when a template becomes visible.</summary>
    event EventHandler<CarTemplateEventArgs> TemplateAppeared;

    /// <summary>Raised when a template stops being visible.</summary>
    event EventHandler<CarTemplateEventArgs> TemplateDisappeared;

    /// <summary>Raised when the appearance changes.</summary>
    event EventHandler<CarContentStyle> ContentStyleChanged;

    /// <summary>Raised when the car disconnects.</summary>
    event EventHandler Disconnected;

    /// <summary>Returns the native object behind the session: <c>CPInterfaceController</c> on CarPlay, <c>CarContext</c> on Android.</summary>
    /// <typeparam name="TNative">The native type.</typeparam>
    /// <returns>The native object, or <see langword="null"/> when the type does not match.</returns>
    TNative? GetNative<TNative>() where TNative : class;
}

/// <summary>What the connected platform and version support.</summary>
public interface ICarCapabilities
{
    /// <summary>Gets the platform.</summary>
    CarPlatform Platform { get; }

    /// <summary>Gets the iOS version on CarPlay.</summary>
    Version? IosVersion { get; }

    /// <summary>Gets the Car App API level of the host on Android.</summary>
    int? CarAppApiLevel { get; }

    /// <summary>Returns whether the platform renders the template kind natively.</summary>
    /// <param name="kind">The template kind.</param>
    /// <returns><see langword="true"/> when supported without a fallback.</returns>
    bool Supports(CarTemplateKind kind);

    /// <summary>Returns whether the platform renders the template type natively.</summary>
    /// <typeparam name="TTemplate">A concrete template type.</typeparam>
    /// <returns><see langword="true"/> when supported without a fallback.</returns>
    bool Supports<TTemplate>() where TTemplate : CarTemplate => Supports(CarTemplateKinds.Of<TTemplate>());

    /// <summary>
    /// Returns whether the connected platform, its version, the CarKit build in the app and the car support a feature. CarKit
    /// sends templates without unsupported features; check this to show something else instead.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <returns><see langword="true"/> when the car shows the feature.</returns>
    bool Supports(CarFeature feature) => false;

    /// <summary>Returns a content limit read from the platform at runtime, or the rule default.</summary>
    /// <param name="kind">The limit.</param>
    /// <returns>The maximum, or <see langword="null"/> when unlimited or unknown.</returns>
    int? GetLimit(CarLimitKind kind);
}

/// <summary>Data of <see cref="ICarSession.TemplateAppeared"/> and <see cref="ICarSession.TemplateDisappeared"/>.</summary>
public sealed class CarTemplateEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="template">The template.</param>
    /// <param name="animated">Whether the change was animated.</param>
    public CarTemplateEventArgs(CarTemplate template, bool animated)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        Animated = animated;
    }

    /// <summary>Gets the template.</summary>
    public CarTemplate Template { get; }

    /// <summary>Gets a value indicating whether the change was animated.</summary>
    public bool Animated { get; }
}

internal static class CarTemplateKinds
{
    public static CarTemplateKind Of<TTemplate>() where TTemplate : CarTemplate
    {
        var type = typeof(TTemplate);
#pragma warning disable CARKIT0001
        return type == typeof(CarListTemplate) ? CarTemplateKind.List
            : type == typeof(CarGridTemplate) ? CarTemplateKind.Grid
            : type == typeof(CarTabTemplate) ? CarTemplateKind.Tab
            : type == typeof(CarInformationTemplate) ? CarTemplateKind.Information
            : type == typeof(CarSearchTemplate) ? CarTemplateKind.Search
            : type == typeof(CarPointOfInterestTemplate) ? CarTemplateKind.PointOfInterest
            : type == typeof(CarAlertTemplate) ? CarTemplateKind.Alert
            : type == typeof(CarMessageTemplate) ? CarTemplateKind.Message
            : type == typeof(CarNowPlayingTemplate) ? CarTemplateKind.NowPlaying
            : type == typeof(CarMapTemplate) ? CarTemplateKind.Map
            : type == typeof(CarActionSheetTemplate) ? CarTemplateKind.ActionSheet
            : type == typeof(CarLongMessageTemplate) ? CarTemplateKind.LongMessage
            : type == typeof(CarVoiceControlTemplate) ? CarTemplateKind.VoiceControl
            : type == typeof(CarPlayContactTemplate) ? CarTemplateKind.CarPlayContact
            : type == typeof(AndroidAutoSignInTemplate) ? CarTemplateKind.AndroidAutoSignIn
            : type == typeof(CarPlayVideoBrowseTemplate) ? CarTemplateKind.CarPlayVideoBrowse
            : throw new ArgumentException($"{type.Name} is not a concrete template type.", nameof(TTemplate));
#pragma warning restore CARKIT0001
    }
}
