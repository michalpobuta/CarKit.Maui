using AndroidX.Car.App.Media.Model;
using CarKit.Maui.Renderers;

namespace CarKit.Maui.Media;

/// <summary>Escape hatch for the Android Auto media playback template (templated media, Car App API 8).</summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread right before it builds the
/// <see cref="MediaPlaybackTemplate"/>, on every rebuild, only when <see cref="CarMediaBuilder.UseTemplatedMediaOnAndroid"/>
/// is on. Keep it idempotent and fast and do not call <c>Build()</c>.
/// </remarks>
public static class AndroidAutoMediaTemplateExtensions
{
    /// <summary>Configures the <see cref="MediaPlaybackTemplate.Builder"/> of the Now Playing screen.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarNowPlayingTemplate ConfigureAndroidAuto(this CarNowPlayingTemplate template, Action<MediaPlaybackTemplate.Builder> configure)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        // Like AndroidAutoTemplateExtensions: an Action<Java.Lang.Object> with a target that filters the builder type
        var hook = new CarAppTypedHook<MediaPlaybackTemplate.Builder>(configure);
        return template.WithPlatformHook(CarPlatform.AndroidAuto, new Action<Java.Lang.Object>(hook.Invoke));
    }
}
