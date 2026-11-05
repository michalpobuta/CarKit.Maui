using CarKit.Maui;
using CarKit.Maui.Navigation;
using Foundation;
using UIKit;

namespace Smoke;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new ContentPage { Content = new Label { Text = "CarKit smoke" } });
}

public sealed class Handler : ICarAppHandler
{
    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => session.SetRootAsync(new CarMapTemplate { TrailingBarButtons = [new CarBarButton("Destination")] });
}

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseCarKit(car => car.UseHandler<Handler>().UseNavigation(nav => nav.ShareRoutesWithCar = true));
        return builder.Build();
    }
}

[Register("AppDelegate")]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
