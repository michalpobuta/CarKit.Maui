using CarKit.Maui;
using CarKit.Maui.Media;
using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Media;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        // UseMedia registers the library and the player (the same object for ICarMediaPlayer and SampleMediaPlayer)
        builder.Services.AddSingleton<ISampleAudioOutput, SampleAudioOutput>();
        builder.Services.AddSingleton<MainPage>();

        // App with the media library only: no UseHandler. Auto-browse builds the CarPlay screens, Android Auto draws
        // the library from the Media3 service (Audio category from the .csproj)
        builder.UseCarKit(car => car.UseMedia(media =>
        {
            media.UseLibrary<SampleMediaLibrary>().UsePlayer<SampleMediaPlayer>().UseAutoBrowse();
            media.PlaybackRates.Add(1.25f);
            media.PlaybackRates.Add(1.5f);
            media.PlaybackRates.Add(2f);
            media.NowPlaying = SampleNowPlaying.Template;
        }));

#if DEBUG
        builder.Logging.AddDebug();
#endif
#if ANDROID
        builder.Logging.AddProvider(new LogcatLoggerProvider());
#elif IOS
        builder.Logging.AddProvider(new ConsoleLoggerProvider());
#endif

        return builder.Build();
    }
}
