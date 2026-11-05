using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.Navigation;

public static partial class CarKitNavigationBuilderExtensions
{
    // iOS: CPMapTemplate renderer (CarKit.Maui module), CarPlay scenes (window, Dashboard, cluster) and
    // AVSpeechSynthesizer voice
    static partial void RegisterPlatform(IServiceCollection services, CarNavigationOptions options)
    {
        services.AddSingleton(sp => new CarPlayNavigationModule(sp.GetRequiredService<CarNavigationRuntime>()));
        services.AddSingleton<ICarPlayNavigationModule>(static sp => sp.GetRequiredService<CarPlayNavigationModule>());
        services.AddSingleton<ICarPlayModuleRenderer>(static sp => new MapTemplateRenderer(sp.GetRequiredService<CarPlayNavigationModule>()));
        services.AddSingleton<ICarSpeechEngine>(static _ => new CarSpeechEngine());
        services.AddSingleton<ICarAudioDucking>(static sp => new CarAudioDucking(sp.GetRequiredService<CarNavigationRuntime>().Logger));
    }
}
