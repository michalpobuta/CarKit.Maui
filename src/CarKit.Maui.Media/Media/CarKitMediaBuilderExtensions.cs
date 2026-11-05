using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarKit.Maui.Media;

/// <summary>Adds the media module to CarKit.</summary>
public static partial class CarKitMediaBuilderExtensions
{
    /// <summary>
    /// Adds the media library and Now Playing: the CarPlay Now Playing bridge and audio session, the Android Auto
    /// media library service, and interruption handling on both platforms.
    /// </summary>
    /// <param name="car">The CarKit builder.</param>
    /// <param name="configure">Configures the module; call <see cref="CarMediaBuilder.UseLibrary{TLibrary}"/> and <see cref="CarMediaBuilder.UsePlayer{TPlayer}"/>.</param>
    /// <returns>The CarKit builder.</returns>
    public static CarKitBuilder UseMedia(this CarKitBuilder car, Action<CarMediaBuilder> configure) =>
        UseMedia(car, configure, CarKitMediaBuildConfiguration.TemplatedMedia);

    internal static CarKitBuilder UseMedia(CarKitBuilder car, Action<CarMediaBuilder> configure, bool templatedMediaFromBuild)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(configure);
        car.AddModule(nameof(UseMedia));
        var media = new CarMediaBuilder(car.Services, templatedMediaFromBuild);
        configure(media);
        var options = media.Build();

        var services = car.Services;
        media.LibraryRegistration!(services);
        media.PlayerRegistration!(services);
        services.AddSingleton(options);
        services.AddSingleton(sp => new CarMediaController(
            sp.GetRequiredService<ICarMediaPlayer>(),
            options,
            sp.GetRequiredService<ICarErrorHandler>(),
            sp.GetRequiredService<ICarDispatcher>(),
            TimeProvider.System));
        services.AddSingleton(sp => new CarMediaRuntime(
            sp.GetRequiredService<ICarMediaLibrary>(),
            sp.GetRequiredService<CarMediaController>(),
            options,
            sp.GetRequiredService<ICarErrorHandler>(),
            (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("CarKit.Media")));
        if (options.AutoBrowse)
        {
            car.UseModuleHandler("media.UseAutoBrowse()", static s => s.AddSingleton<ICarAppHandler, CarMediaAutoBrowseHandler>());
        }

        RegisterPlatform(services, options);
        return car;
    }

    // Platforms add the Now Playing bridge, audio session, focus and module renderers
    // (Platforms/*/CarKitMediaPlatform.*.cs)
    static partial void RegisterPlatform(IServiceCollection services, CarMediaOptions options);
}
