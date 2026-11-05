using CarPlay;

namespace CarKit.Maui.Media;

/// <summary>Escape hatch for the CarPlay Now Playing screen.</summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread after it configures
/// <see cref="CPNowPlayingTemplate.SharedTemplate"/> and after every update, including button refreshes when the
/// shuffle or repeat state changes. Keep it idempotent and fast and do not change the observers, buttons or
/// <c>UserInfo</c> that CarKit sets. CarKit never sets <see cref="CPNowPlayingTemplate.NowPlayingMode"/>, so the
/// callback can switch to the sports mode (<c>CPNowPlayingModeSports</c>, iOS 18.4 and later).
/// </remarks>
public static class CarPlayMediaTemplateExtensions
{
    /// <summary>Configures the shared <see cref="CPNowPlayingTemplate"/> of the Now Playing screen.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarNowPlayingTemplate ConfigureCarPlay(this CarNowPlayingTemplate template, Action<CPNowPlayingTemplate> configure)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        return template.WithPlatformHook(CarPlatform.CarPlay, configure);
    }
}
