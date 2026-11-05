using CarPlay;

namespace CarKit.Maui.Navigation;

/// <summary>Escape hatch for the CarPlay map template.</summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread after it builds the <see cref="CPMapTemplate"/> and after every
/// update. Keep it idempotent and fast and do not replace <c>MapDelegate</c>, the map buttons' handlers or <c>UserInfo</c>
/// that CarKit sets; trip previews and the navigation session come from <see cref="ICarNavigationSession"/>.
/// </remarks>
public static class CarPlayNavigationTemplateExtensions
{
    /// <summary>Configures the <see cref="CPMapTemplate"/> of the map.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarMapTemplate ConfigureCarPlay(this CarMapTemplate template, Action<CPMapTemplate> configure)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        return template.WithPlatformHook(CarPlatform.CarPlay, configure);
    }
}
