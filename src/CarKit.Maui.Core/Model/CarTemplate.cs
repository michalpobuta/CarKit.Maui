namespace CarKit.Maui;

/// <summary>Identifies each template type; platform rules are keyed by it.</summary>
public enum CarTemplateKind
{
    /// <summary><see cref="CarListTemplate"/>.</summary>
    List,

    /// <summary><see cref="CarGridTemplate"/>.</summary>
    Grid,

    /// <summary><see cref="CarTabTemplate"/>.</summary>
    Tab,

    /// <summary><see cref="CarInformationTemplate"/>.</summary>
    Information,

    /// <summary><see cref="CarSearchTemplate"/>.</summary>
    Search,

    /// <summary><see cref="CarPointOfInterestTemplate"/>.</summary>
    PointOfInterest,

    /// <summary><see cref="CarAlertTemplate"/>.</summary>
    Alert,

    /// <summary><see cref="CarMessageTemplate"/>.</summary>
    Message,

    /// <summary><see cref="CarNowPlayingTemplate"/>.</summary>
    NowPlaying,

    /// <summary><see cref="CarMapTemplate"/>.</summary>
    Map,

    /// <summary><see cref="CarActionSheetTemplate"/>.</summary>
    ActionSheet,

    /// <summary><see cref="CarLongMessageTemplate"/>.</summary>
    LongMessage,

    /// <summary><see cref="CarVoiceControlTemplate"/>.</summary>
    VoiceControl,

    /// <summary><see cref="CarPlayContactTemplate"/>.</summary>
    CarPlayContact,

    /// <summary><see cref="AndroidAutoSignInTemplate"/>.</summary>
    AndroidAutoSignIn,

    /// <summary>CarPlay video browsing template (iOS 27, experimental).</summary>
    CarPlayVideoBrowse,
}

/// <summary>
/// Base of all templates. Templates are immutable: build a new one with a <see langword="with"/>
/// expression and pass it to <see cref="ICarSession.UpdateAsync(CarTemplate)"/>.
/// </summary>
public abstract record CarTemplate : CarElement
{
    private protected CarTemplate()
    {
    }

    /// <summary>Gets the template type.</summary>
    public abstract CarTemplateKind Kind { get; }

    /// <summary>Gets the title.</summary>
    public CarText? Title { get; init; }

    /// <summary>Gets how the template appears as a tab.</summary>
    public CarTabInfo? TabInfo { get; init; }

    /// <summary>Gets the leading navigation bar buttons (CarPlay).</summary>
    public CarArray<CarBarButton> LeadingBarButtons { get; init; }

    /// <summary>Gets the trailing navigation bar buttons (CarPlay) or header actions (Android Auto).</summary>
    public CarArray<CarBarButton> TrailingBarButtons { get; init; }

    /// <summary>Gets the platform configuration callbacks added by <c>ConfigureCarPlay</c> and <c>ConfigureAndroidAuto</c>.</summary>
    public CarPlatformHooks PlatformHooks { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); } = CarPlatformHooks.None;

    /// <summary>Gets the template used when the platform does not support this one.</summary>
    public CarTemplate? Fallback { get; init; }
}

/// <summary>Base of templates shown with <see cref="ICarSession.PresentAsync(CarModalTemplate, bool)"/>.</summary>
public abstract record CarModalTemplate : CarTemplate
{
    private protected CarModalTemplate()
    {
    }
}

/// <summary>Platform callbacks run by the renderer after it builds the native template.</summary>
public sealed record CarPlatformHooks
{
    /// <summary>Gets an instance without callbacks.</summary>
    public static CarPlatformHooks None { get; } = new();

    /// <summary>Gets the CarPlay callback.</summary>
    public Delegate? CarPlay { get; init; }

    /// <summary>Gets the Android Auto and Android Automotive callback.</summary>
    public Delegate? AndroidAuto { get; init; }

    /// <summary>Returns a copy with <paramref name="hook"/> combined into the platform's callback.</summary>
    /// <param name="platform">The platform.</param>
    /// <param name="hook">The callback; the same delegate type as earlier callbacks for that platform.</param>
    /// <returns>The new hooks.</returns>
    public CarPlatformHooks Add(CarPlatform platform, Delegate hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        return platform == CarPlatform.CarPlay
            ? this with { CarPlay = Delegate.Combine(CarPlay, hook) }
            : this with { AndroidAuto = Delegate.Combine(AndroidAuto, hook) };
    }
}

/// <summary>Extension methods for templates.</summary>
public static class CarTemplateExtensions
{
    /// <summary>Returns a copy that uses <paramref name="fallback"/> where the platform does not support the template.</summary>
    /// <typeparam name="TTemplate">The template type.</typeparam>
    /// <param name="template">The template.</param>
    /// <param name="fallback">The fallback template.</param>
    /// <returns>The new template.</returns>
    public static TTemplate WithFallback<TTemplate>(this TTemplate template, CarTemplate fallback)
        where TTemplate : CarTemplate
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(fallback);
        if (ReferenceEquals(template, fallback))
        {
            throw new ArgumentException("A template cannot be its own fallback.", nameof(fallback));
        }

        return (TTemplate)((CarTemplate)template with { Fallback = fallback });
    }

    // Used by ConfigureCarPlay / ConfigureAndroidAuto in the platform assemblies
    internal static TTemplate WithPlatformHook<TTemplate>(this TTemplate template, CarPlatform platform, Delegate hook)
        where TTemplate : CarTemplate =>
        (TTemplate)((CarTemplate)template with { PlatformHooks = template.PlatformHooks.Add(platform, hook) });
}
