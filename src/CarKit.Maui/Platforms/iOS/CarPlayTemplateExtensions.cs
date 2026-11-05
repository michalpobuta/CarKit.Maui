using CarPlay;

namespace CarKit.Maui;

/// <summary>
/// Escape hatch for CarPlay: configures the native <c>CP*</c> template beyond what the CarKit model describes.
/// </summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread every time it renders the template: after it builds the
/// native object, after every in-place update (including image refreshes after a light or dark mode change), after an
/// image that loads later changes the native object and, for tabs, after it applies <see cref="CarTemplate.TabInfo"/>. Keep the callback idempotent and fast, do not call
/// <see cref="ICarSession"/> from it, and do not change <c>UserInfo</c>, delegates or handlers that CarKit sets.
/// Exceptions go to <see cref="ICarErrorHandler"/>; the template is still shown. Built-in fallbacks do not carry the
/// callback; an explicit fallback keeps its own.
/// </remarks>
public static class CarPlayTemplateExtensions
{
    /// <summary>Configures the <see cref="CPListTemplate"/> of the list.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarListTemplate ConfigureCarPlay(this CarListTemplate template, Action<CPListTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPGridTemplate"/> of the grid.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarGridTemplate ConfigureCarPlay(this CarGridTemplate template, Action<CPGridTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPTabBarTemplate"/> of the tabs.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarTabTemplate ConfigureCarPlay(this CarTabTemplate template, Action<CPTabBarTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPInformationTemplate"/> of the information panel.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarInformationTemplate ConfigureCarPlay(this CarInformationTemplate template, Action<CPInformationTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPSearchTemplate"/> of the search.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarSearchTemplate ConfigureCarPlay(this CarSearchTemplate template, Action<CPSearchTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPInformationTemplate"/> that shows the message on CarPlay.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarMessageTemplate ConfigureCarPlay(this CarMessageTemplate template, Action<CPInformationTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPAlertTemplate"/> of the alert.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarAlertTemplate ConfigureCarPlay(this CarAlertTemplate template, Action<CPAlertTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPActionSheetTemplate"/> of the action sheet.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarActionSheetTemplate ConfigureCarPlay(this CarActionSheetTemplate template, Action<CPActionSheetTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPVoiceControlTemplate"/> of the voice control template.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarVoiceControlTemplate ConfigureCarPlay(this CarVoiceControlTemplate template, Action<CPVoiceControlTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPPointOfInterestTemplate"/> of the point-of-interest template.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback; do not replace <c>PointOfInterestDelegate</c> or the points' <c>UserInfo</c>.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarPointOfInterestTemplate ConfigureCarPlay(this CarPointOfInterestTemplate template, Action<CPPointOfInterestTemplate> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="CPContactTemplate"/> of the contact card.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarPlayContactTemplate ConfigureCarPlay(this CarPlayContactTemplate template, Action<CPContactTemplate> configure) => Add(template, configure);

    // A typed delegate in PlatformHooks.CarPlay: one native type per template, so Delegate.Combine always succeeds
    private static TTemplate Add<TTemplate>(TTemplate template, Delegate configure)
        where TTemplate : CarTemplate
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        return template.WithPlatformHook(CarPlatform.CarPlay, configure);
    }
}
