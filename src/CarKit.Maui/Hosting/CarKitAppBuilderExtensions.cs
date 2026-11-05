using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Hosting;

namespace CarKit.Maui;

/// <summary>Adds CarKit to a MAUI app.</summary>
public static partial class CarKitAppBuilderExtensions
{
    /// <summary>
    /// Adds CarPlay and Android Auto support: registers the options, the app's <see cref="ICarAppHandler"/>
    /// and the services the car scene (iOS) and car app service (Android) use when a car connects.
    /// </summary>
    /// <param name="builder">The MAUI app builder.</param>
    /// <param name="configure">Configures CarKit; call <see cref="CarKitBuilder.UseHandler{THandler}"/> here.</param>
    /// <returns>The builder.</returns>
    public static MauiAppBuilder UseCarKit(this MauiAppBuilder builder, Action<CarKitBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Register(builder.Services, configure, CarKitBuildConfiguration.Categories);
        ConfigurePlatform(builder);
        return builder;
    }

    internal static void Register(IServiceCollection services, Action<CarKitBuilder> configure, CarAppCategory? buildCategories)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(d => d.ServiceType == typeof(CarKitRuntime)))
        {
            throw new InvalidOperationException("UseCarKit was already called.");
        }

        var car = new CarKitBuilder(services, buildCategories);
        configure(car);
        Validate(car, services);

        services.AddSingleton(car.Options);
        services.AddSingleton(car.Android);
        (car.HandlerRegistration ?? car.ModuleHandler?.Registration)?.Invoke(services);
        services.TryAddSingleton<ICarDispatcher, MauiCarDispatcher>();
        services.TryAddSingleton<ICarErrorHandler>(sp => new LoggingCarErrorHandler(LoggerFactory(sp).CreateLogger("CarKit.Session")));
        services.AddSingleton(sp => new CarKitRuntime(sp));
    }

    internal static ILoggerFactory LoggerFactory(IServiceProvider services) =>
        services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

    // Platforms add their requirements here (e.g. keeping scene delegates from being trimmed)
    static partial void ConfigurePlatform(MauiAppBuilder builder);

    private static void Validate(CarKitBuilder car, IServiceCollection services)
    {
        if (car.ProjectCategories is { } fromProject)
        {
            if (car.Categories != fromProject)
            {
                throw new InvalidOperationException(
                    $"car.Categories ({car.Categories}) differs from <CarKitCategories> in the app project file ({fromProject}). "
                    + "The project file drives the generated entitlements and manifest entries: declare categories there and leave car.Categories unchanged.");
            }
        }
        else if (car.Categories == CarAppCategory.None)
        {
            throw new InvalidOperationException("No CarKit categories: set <CarKitCategories> in the app project file, for example <CarKitCategories>Parking</CarKitCategories>.");
        }

        // The host list applies only to Release builds; an invalid entry should fail at startup in Debug too
        foreach (var entry in car.Android.AllowedHosts)
        {
            CarKitAndroidOptions.ParseAllowedHost(entry);
        }

        var appHandler = car.HandlerRegistration is not null || services.Any(d => d.ServiceType == typeof(ICarAppHandler));
        if (car.ModuleHandler is { Source: var source })
        {
            // One owner of the car screens (spec: "Media module decisions (Stage 5)")
            if (appHandler)
            {
                throw new InvalidOperationException(
                    $"Both car.UseHandler<THandler>() (or a registered ICarAppHandler) and {source} build the car screens; use one of them.");
            }
        }
        else if (!appHandler)
        {
            throw new InvalidOperationException(
                "No ICarAppHandler: call car.UseHandler<THandler>() inside UseCarKit (media apps can call media.UseAutoBrowse() instead).");
        }
    }
}
