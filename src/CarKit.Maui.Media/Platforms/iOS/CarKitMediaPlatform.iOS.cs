using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace CarKit.Maui.Media;

public static partial class CarKitMediaBuilderExtensions
{
    // iOS: the MPNowPlayingInfoCenter/MPRemoteCommandCenter bridge and AVAudioSession from app start (also without
    // CarPlay), the CPNowPlayingTemplate renderer as the CarKit.Maui module renderer
    static partial void RegisterPlatform(IServiceCollection services, CarMediaOptions options)
    {
        services.AddSingleton(sp => new CarPlayNowPlayingBridge(sp.GetRequiredService<CarMediaRuntime>()));
        services.AddSingleton(sp => new CarAudioSession(sp.GetRequiredService<CarMediaRuntime>(), sp.GetRequiredService<ICarDispatcher>()));
        services.AddSingleton<ICarPlayModuleRenderer>(sp => new NowPlayingTemplateRenderer(sp.GetRequiredService<CarMediaRuntime>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, CarMediaInitializer>());
    }
}

// Bridges start when the MauiApp is built (WillFinishLaunching, main thread): Now Playing and interruptions work from
// launch
internal sealed class CarMediaInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        services.GetRequiredService<CarAudioSession>().Start();
        services.GetRequiredService<CarPlayNowPlayingBridge>().Start();
    }
}
