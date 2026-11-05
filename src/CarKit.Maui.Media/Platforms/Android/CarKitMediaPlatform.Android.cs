using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Hosting;

namespace CarKit.Maui.Media;

public static partial class CarKitMediaBuilderExtensions
{
    // Android: audio focus and ACTION_AUDIO_BECOMING_NOISY from app start, the MediaPlaybackTemplate renderer (AA media
    // templates) as the CarKit.Maui module renderer; the Media3 service is created by Android (generated subclass of
    // CarKitMediaLibraryService)
    static partial void RegisterPlatform(IServiceCollection services, CarMediaOptions options)
    {
        services.AddSingleton(sp => new CarAudioFocus(sp.GetRequiredService<CarMediaRuntime>(), sp.GetRequiredService<ICarDispatcher>(), Android.App.Application.Context));
        services.AddSingleton<ICarAppModuleRenderer>(sp => new MediaPlaybackTemplateRenderer(sp.GetRequiredService<CarMediaRuntime>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMauiInitializeService, CarMediaInitializer>());
    }
}

// Audio focus start when the MauiApp is built (MauiApplication.OnCreate, main thread), also without an Activity
internal sealed class CarMediaInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        // media.HandleAudioFocus = false: the app player (e.g. ExoPlayer) drives focus; two requests from one
        // app would take the focus from each other
        if (services.GetRequiredService<CarMediaOptions>().HandleAudioFocus)
        {
            services.GetRequiredService<CarAudioFocus>().Start();
        }
    }
}
