using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarKit.Maui.Navigation;

/// <summary>Adds the navigation module to CarKit.</summary>
public static partial class CarKitNavigationBuilderExtensions
{
    /// <summary>
    /// Adds turn-by-turn navigation: <see cref="ICarNavigationSession"/> (CarPlay map template and navigation session,
    /// Android Auto navigation template and navigation manager), <see cref="ICarMapSurfaces"/> (the map on the car screen,
    /// the CarPlay Dashboard and the instrument cluster) and <see cref="ICarVoicePrompts"/>. The car screens still come from
    /// <c>car.UseHandler&lt;THandler&gt;()</c>; show a <see cref="CarMapTemplate"/> as the root.
    /// </summary>
    /// <param name="car">The CarKit builder.</param>
    /// <param name="configure">Configures the module.</param>
    /// <returns>The CarKit builder.</returns>
    public static CarKitBuilder UseNavigation(this CarKitBuilder car, Action<CarNavigationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(configure);
        car.AddModule(nameof(UseNavigation));
        var navigation = new CarNavigationBuilder(car.Services);
        configure(navigation);
        var options = navigation.Build();

        var services = car.Services;
        navigation.HandlerRegistration?.Invoke(services);
        services.AddSingleton(options);
        services.AddSingleton(sp => new CarNavigationController(
            sp.GetRequiredService<ICarDispatcher>(),
            sp.GetRequiredService<ICarErrorHandler>(),
            () => sp.GetService<ICarNavigationHandler>(),
            Logger(sp)));
        services.AddSingleton<ICarNavigationSession>(static sp => sp.GetRequiredService<CarNavigationController>());
        services.AddSingleton(sp => new CarMapSurfaces(sp.GetRequiredService<ICarDispatcher>(), sp.GetRequiredService<ICarErrorHandler>(), Logger(sp), options.LogFrameTimes));
        services.AddSingleton<ICarMapSurfaces>(static sp => sp.GetRequiredService<CarMapSurfaces>());
        services.AddSingleton(sp => new CarNavigationRuntime(
            sp.GetRequiredService<CarNavigationController>(),
            sp.GetRequiredService<CarMapSurfaces>(),
            options,
            sp.GetRequiredService<CarKitOptions>(),
            sp.GetRequiredService<ICarDispatcher>(),
            sp.GetRequiredService<ICarErrorHandler>(),
            Logger(sp),
            sp));
        // Voice: the platform adds the engine and ducking (ICarSpeechEngine, ICarAudioDucking); the car ending
        // navigation stops the queue
        services.AddSingleton<ICarVoicePrompts>(sp =>
        {
            var queue = new CarVoicePromptQueue(
                sp.GetRequiredService<ICarSpeechEngine>(),
                sp.GetRequiredService<ICarAudioDucking>(),
                sp.GetRequiredService<ICarDispatcher>(),
                sp.GetRequiredService<ICarErrorHandler>(),
                options.VoicePromptLanguage);
            sp.GetRequiredService<CarNavigationController>().EndedByCar += (_, _) => queue.CancelAll();
            return queue;
        });
        RegisterPlatform(services, options);
        return car;
    }

    internal static ILogger Logger(IServiceProvider services) =>
        (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("CarKit.Navigation");

    // Platforms add the map template renderers, surface hosts, scenes and voice
    // (Platforms/*/CarKitNavigationPlatform.*.cs)
    static partial void RegisterPlatform(IServiceCollection services, CarNavigationOptions options);
}

// Everything the module platforms need: navigation session, surfaces, options, categories, dispatcher, errors, log
internal sealed class CarNavigationRuntime(
    CarNavigationController navigation,
    CarMapSurfaces surfaces,
    CarNavigationOptions options,
    CarKitOptions carKit,
    ICarDispatcher dispatcher,
    ICarErrorHandler errors,
    ILogger logger,
    IServiceProvider services)
{
    public CarNavigationController Navigation { get; } = navigation;

    public CarMapSurfaces Surfaces { get; } = surfaces;

    public CarNavigationOptions Options { get; } = options;

    public CarAppCategory Categories => carKit.Categories;

    public ICarDispatcher Dispatcher { get; } = dispatcher;

    public ICarErrorHandler Errors { get; } = errors;

    public ILogger Logger { get; } = logger;

    public IServiceProvider Services { get; } = services;
}

// The three app surfaces (car screen, Dashboard, cluster)
internal sealed class CarMapSurfaces(ICarDispatcher dispatcher, ICarErrorHandler errors, ILogger logger, bool logFrames) : ICarMapSurfaces
{
    public CarMapSurface CarScreen { get; } = new(CarMapSurfaceKind.CarScreen, dispatcher, errors, logger, logFrames);

    public CarMapSurface Dashboard { get; } = new(CarMapSurfaceKind.Dashboard, dispatcher, errors, logger, logFrames);

    public CarMapSurface InstrumentCluster { get; } = new(CarMapSurfaceKind.InstrumentCluster, dispatcher, errors, logger, logFrames);

    ICarMapSurface ICarMapSurfaces.CarScreen => CarScreen;

    ICarMapSurface ICarMapSurfaces.Dashboard => Dashboard;

    ICarMapSurface ICarMapSurfaces.InstrumentCluster => InstrumentCluster;
}
