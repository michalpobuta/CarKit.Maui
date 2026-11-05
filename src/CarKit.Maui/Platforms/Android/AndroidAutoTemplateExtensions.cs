using AndroidX.Car.App.Model;
using AndroidX.Car.App.Model.SignIn;
using CarKit.Maui.Renderers;

namespace CarKit.Maui;

/// <summary>
/// Escape hatch for Android Auto and Android Automotive: configures the Car App Library template builder beyond what
/// the CarKit model describes.
/// </summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread right before it calls <c>Build()</c>, every time it
/// rebuilds the screen (updates, loaded images, tab switches, search results and light or dark mode changes). Keep the
/// callback idempotent and fast, do not call <see cref="ICarSession"/> from it, do not call <c>Build()</c>, and do not
/// change what the host compares for refreshes (title, sections, rows, loading): CarKit counts task steps from the model.
/// Exceptions go to <see cref="ICarErrorHandler"/>; the template is still built. Built-in fallbacks do not carry the
/// callback; an explicit fallback keeps its own. A list is built with <c>ListTemplate.Builder</c> below Car App API 8 and
/// with <c>SectionedItemTemplate.Builder</c> from API 8: configure both with <c>ConfigureAndroidAuto</c> and
/// <c>ConfigureAndroidAutoSectioned</c>. When none of a template's callbacks fits the builder the host uses, CarKit logs a
/// warning once per template.
/// </remarks>
public static class AndroidAutoTemplateExtensions
{
    /// <summary>Configures the <see cref="ListTemplate.Builder"/> used for the list below Car App API 8 (or when <c>SectionedItemTemplate</c> is switched off).</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarListTemplate ConfigureAndroidAuto(this CarListTemplate template, Action<ListTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="SectionedItemTemplate.Builder"/> used for the list from Car App API 8.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarListTemplate ConfigureAndroidAutoSectioned(this CarListTemplate template, Action<SectionedItemTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="GridTemplate.Builder"/> of the grid.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarGridTemplate ConfigureAndroidAuto(this CarGridTemplate template, Action<GridTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="TabTemplate.Builder"/> of the tabs.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarTabTemplate ConfigureAndroidAuto(this CarTabTemplate template, Action<TabTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="PaneTemplate.Builder"/> of the information panel.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarInformationTemplate ConfigureAndroidAuto(this CarInformationTemplate template, Action<PaneTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="SearchTemplate.Builder"/> of the search.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarSearchTemplate ConfigureAndroidAuto(this CarSearchTemplate template, Action<SearchTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="MessageTemplate.Builder"/> of the message.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarMessageTemplate ConfigureAndroidAuto(this CarMessageTemplate template, Action<MessageTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="LongMessageTemplate.Builder"/> of the long message.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarLongMessageTemplate ConfigureAndroidAuto(this CarLongMessageTemplate template, Action<LongMessageTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="Alert.Builder"/> of the alert (Car App API 5; below it the alert is a message screen without the callback).</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarAlertTemplate ConfigureAndroidAuto(this CarAlertTemplate template, Action<Alert.Builder> configure) => Add(template, configure);

    /// <summary>
    /// Configures the <see cref="PlaceListMapTemplate.Builder"/> of the point-of-interest template. CarKit uses it when the
    /// app declares a point-of-interest category (Parking, EV charging, fueling); in other categories the places are a
    /// <see cref="ListTemplate"/> and the callback does not run.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback; do not change the item list, which CarKit compares for refreshes.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarPointOfInterestTemplate ConfigureAndroidAuto(this CarPointOfInterestTemplate template, Action<PlaceListMapTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="SignInTemplate.Builder"/> of the sign-in screen.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static AndroidAutoSignInTemplate ConfigureAndroidAuto(this AndroidAutoSignInTemplate template, Action<SignInTemplate.Builder> configure) => Add(template, configure);

    // One delegate type per platform in PlatformHooks (Delegate.Combine), and a list has two builders: we store an
    // Action<Java.Lang.Object> that calls the typed callback only for its builder (target CarAppTypedHook; the renderer
    // uses it to recognize a hook that does not fit the builder, and warns)
    private static TTemplate Add<TTemplate, TBuilder>(TTemplate template, Action<TBuilder> configure)
        where TTemplate : CarTemplate
        where TBuilder : Java.Lang.Object
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        Action<Java.Lang.Object> hook = new CarAppTypedHook<TBuilder>(configure).Invoke;
        return template.WithPlatformHook(CarPlatform.AndroidAuto, hook);
    }
}
