using AndroidX.Car.App.Navigation.Model;
using CarKit.Maui.Renderers;

namespace CarKit.Maui.Navigation;

/// <summary>Escape hatch for the Android Auto navigation templates.</summary>
/// <remarks>
/// CarKit calls the callback synchronously on the main thread right before it builds the template, on every rebuild (every
/// navigation change rebuilds the map template). Keep it idempotent and fast and do not call <c>Build()</c>. The map
/// template uses <see cref="NavigationTemplate.Builder"/> except while trip previews are shown, when it uses
/// <see cref="MapWithContentTemplate.Builder"/> (Car App API 7; older hosts get the deprecated route preview template
/// without hooks).
/// </remarks>
public static class AndroidAutoNavigationTemplateExtensions
{
    /// <summary>Configures the <see cref="NavigationTemplate.Builder"/> of the map.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarMapTemplate ConfigureAndroidAuto(this CarMapTemplate template, Action<NavigationTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="MapWithContentTemplate.Builder"/> of the map while trip previews are shown.</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarMapTemplate ConfigureAndroidAutoMapWithContent(this CarMapTemplate template, Action<MapWithContentTemplate.Builder> configure) => Add(template, configure);

    /// <summary>Configures the <see cref="MapWithContentTemplate.Builder"/> of the places on the app's map (Car App API 7, map surface content).</summary>
    /// <param name="template">The template.</param>
    /// <param name="configure">The callback.</param>
    /// <returns>A copy with the callback added.</returns>
    public static CarPointOfInterestTemplate ConfigureAndroidAutoMapWithContent(this CarPointOfInterestTemplate template, Action<MapWithContentTemplate.Builder> configure) =>
        Add(template, configure);

    // Like AndroidAutoTemplateExtensions: an Action<Java.Lang.Object> with a target that filters the builder type
    private static TTemplate Add<TTemplate, TBuilder>(TTemplate template, Action<TBuilder> configure)
        where TTemplate : CarTemplate
        where TBuilder : Java.Lang.Object
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(configure);
        var hook = new CarAppTypedHook<TBuilder>(configure);
        return template.WithPlatformHook(CarPlatform.AndroidAuto, new Action<Java.Lang.Object>(hook.Invoke));
    }
}
