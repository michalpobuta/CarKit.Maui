using CarKit.Maui.Communication;
using CarKit.Maui.Media;
using CarKit.Maui.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.DeviceTests;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        // CarKit callback errors visible in the test result (the default handler only logs)
        builder.Services.AddSingleton<ICarErrorHandler>(DeviceTestErrors.Instance);
        // Stage 5: the Media module with a test library and player (the Audio category from the .csproj); the media tests
        // exercise the real bridges (MPNowPlayingInfoCenter, AVAudioSession, the Media3 service, audio focus) built by
        // UseMedia
        // Stage 8: the Navigation module (navigation session, surfaces, voice) with a test handler
        builder.UseCarKit(car => car.UseNavigation(nav => nav.UseHandler<DeviceTestNavigationHandler>()).UseCommunication(c => c.UseMessageHandler<DeviceTestMessageHandler>().UseCallHandler<DeviceTestCallHandler>()).UseMedia(media =>
        {
            media.UseLibrary<DeviceTestMediaLibrary>().UsePlayer<DeviceTestMediaPlayer>().UseAutoBrowse();
            media.PlaybackRates.Add(1.5f);
            media.PlaybackRates.Add(2f);
            // Review C1: buttons without an Android icon (More, Custom without a drawable) must not bring down the Media3 session
            media.NowPlaying = new CarNowPlayingTemplate
            {
                Buttons =
                [
                    new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle) { Id = "shuffle" },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.Repeat) { Id = "repeat" },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.PlaybackRate) { Id = "rate" },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.AddToLibrary) { Id = "add" },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.More) { Id = "more" },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "no-icon", Image = CarImage.FromSystemSymbol("heart", null) },
                    new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "like", Image = CarImage.FromSystemSymbol("heart", "ic_carkit_test") },
                ],
            };
        }));
        return builder.Build();
    }
}

// Errors from the test app's ICarErrorHandler (Now Playing bridge, Media3 service, auto-browse)
internal sealed class DeviceTestErrors : ICarErrorHandler
{
    private readonly List<string> _errors = [];

    public static DeviceTestErrors Instance { get; } = new();

    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    public void HandleError(Exception exception, CarErrorContext context)
    {
        lock (_errors)
        {
            _errors.Add($"{context.Operation}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public override string ToString() => string.Join(" | ", Errors);
}
