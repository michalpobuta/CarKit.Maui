using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.Navigation;

public static partial class CarKitNavigationBuilderExtensions
{
    // Android: map and POI renderer with the app map (CarKit.Maui module), cluster session, TTS with transient focus
    static partial void RegisterPlatform(IServiceCollection services, CarNavigationOptions options)
    {
        services.AddSingleton(sp => new CarAppNavigationModule(sp.GetRequiredService<CarNavigationRuntime>()));
        services.AddSingleton<ICarAppModuleRenderer>(static sp => sp.GetRequiredService<CarAppNavigationModule>());
        services.AddSingleton<ICarAppClusterSessionFactory>(static sp => sp.GetRequiredService<CarAppNavigationModule>());
        services.AddSingleton<ICarSpeechEngine>(static _ => new CarSpeechEngine(Android.App.Application.Context));
        services.AddSingleton<ICarAudioDucking>(static _ => new CarAudioDucking(Android.App.Application.Context));
    }
}
