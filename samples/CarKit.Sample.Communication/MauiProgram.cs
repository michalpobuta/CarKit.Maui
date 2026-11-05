using CarKit.Maui;
using CarKit.Maui.Communication;
using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Communication;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<SampleConversations>();
        builder.Services.AddSingleton<MainPage>();

        // Car screens come from the app handler; Communication module: notifications with reply, Siri (App Group from
        // the .csproj), CallKit on iOS (Communication category from the .csproj)
        builder.UseCarKit(car => car
            .UseHandler<SampleCommunicationCarHandler>()
            .UseCommunication(communication => communication
                .UseMessageHandler<SampleMessageHandler>()
                .UseCallHandler<SampleCallHandler>()));

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
