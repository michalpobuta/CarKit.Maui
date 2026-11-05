using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace CarKit.Maui.Communication;

public static partial class CarKitCommunicationBuilderExtensions
{
    // iOS: notification category allowed in CarPlay and the UNUserNotificationCenter delegate, the Siri bridge (App
    // Group store, Darwin notification) and CallKit; starts when MauiApp is built (IMauiInitializeService), also
    // without CarPlay
    static partial void RegisterPlatform(IServiceCollection services, CarCommunicationOptions options)
    {
        services.AddSingleton(sp => new CarSiriBridge(sp.GetRequiredService<CarCommunicationRuntime>()));
        services.AddSingleton(sp => new CarIosMessageNotifier(sp.GetRequiredService<CarCommunicationRuntime>(), sp.GetRequiredService<CarSiriBridge>()));
        services.AddSingleton<ICarMessageNotifier>(static sp => sp.GetRequiredService<CarIosMessageNotifier>());
        services.AddSingleton(sp => new CarCallKitManager(sp.GetRequiredService<CarCommunicationRuntime>()));
        services.AddSingleton<ICarCallManager>(static sp => sp.GetRequiredService<CarCallKitManager>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, CarCommunicationInitializer>());
    }
}

// Starts when MauiApp is built (WillFinishLaunching, main thread): the category and delegate must be set before the
// system delivers the reply from the notification that launched the app
internal sealed class CarCommunicationInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        services.GetRequiredService<CarIosMessageNotifier>().Start();
        services.GetRequiredService<CarSiriBridge>().Start();
    }
}
