using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarKit.Maui.Communication;

/// <summary>Adds the communication module to CarKit.</summary>
public static partial class CarKitCommunicationBuilderExtensions
{
    /// <summary>
    /// Adds messaging and calls: <see cref="ICarMessageNotifier"/> (Android <c>MessagingStyle</c> notifications, iOS
    /// notifications allowed in CarPlay, the Siri store of the Intents Extension) and <see cref="ICarCallManager"/>
    /// (CallKit on iOS). The car screens still come from <c>car.UseHandler&lt;THandler&gt;()</c>.
    /// </summary>
    /// <param name="car">The CarKit builder.</param>
    /// <param name="configure">Configures the module.</param>
    /// <returns>The CarKit builder.</returns>
    public static CarKitBuilder UseCommunication(this CarKitBuilder car, Action<CarCommunicationBuilder> configure) =>
        UseCommunication(car, configure, CarCommunicationBuildConfiguration.AppGroup);

    internal static CarKitBuilder UseCommunication(CarKitBuilder car, Action<CarCommunicationBuilder> configure, string? appGroupFromBuild)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(configure);
        car.AddModule(nameof(UseCommunication));
        var communication = new CarCommunicationBuilder(car.Services);
        configure(communication);
        var options = communication.Build(appGroupFromBuild);

        var services = car.Services;
        communication.MessageHandlerRegistration?.Invoke(services);
        communication.CallHandlerRegistration?.Invoke(services);
        services.AddSingleton(options);
        services.AddSingleton(sp => new CarMessageRouter(sp, sp.GetRequiredService<ICarErrorHandler>(), sp.GetRequiredService<ICarDispatcher>()));
        services.AddSingleton(sp => new CarCallRegistry(sp.GetRequiredService<ICarDispatcher>()));
        services.AddSingleton(sp => new CarCommunicationRuntime(
            options,
            sp.GetRequiredService<CarMessageRouter>(),
            sp.GetRequiredService<CarCallRegistry>(),
            sp.GetService<ICarCallHandler>() ?? CarDefaultCallHandler.Instance,
            sp.GetRequiredService<ICarErrorHandler>(),
            sp.GetRequiredService<ICarDispatcher>(),
            (sp.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("CarKit.Communication")));

        RegisterPlatform(services, options);
        // A platform without calls (Android until the Core-Telecom binding) gets a manager without support
        services.TryAddSingleton<ICarCallManager>(static _ => CarUnsupportedCallManager.Instance);
        return car;
    }

    // The platforms add notifications, the Siri/App Group bridge and CallKit
    // (Platforms/*/CarKitCommunicationPlatform.*.cs)
    static partial void RegisterPlatform(IServiceCollection services, CarCommunicationOptions options);
}

// Everything the module's platforms need: options, message router, call registry, the app's call handler
internal sealed class CarCommunicationRuntime(
    CarCommunicationOptions options,
    CarMessageRouter messages,
    CarCallRegistry calls,
    ICarCallHandler callHandler,
    ICarErrorHandler errors,
    ICarDispatcher dispatcher,
    ILogger logger)
{
    public CarCommunicationOptions Options { get; } = options;

    public CarMessageRouter Messages { get; } = messages;

    public CarCallRegistry Calls { get; } = calls;

    public ICarCallHandler CallHandler { get; } = callHandler;

    public ICarErrorHandler Errors { get; } = errors;

    public ICarDispatcher Dispatcher { get; } = dispatcher;

    public ILogger Logger { get; } = logger;
}

// A call handler without an app implementation: accepts every system action (default interface methods)
internal sealed class CarDefaultCallHandler : ICarCallHandler
{
    public static readonly CarDefaultCallHandler Instance = new();
}
