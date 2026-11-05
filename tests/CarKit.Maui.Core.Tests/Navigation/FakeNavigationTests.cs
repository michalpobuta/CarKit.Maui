using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Navigation;

// Stage 8: FakeCarHost.Navigation — recording of platform calls and driver simulation
// (spec: "Navigation module decisions (Stage 8)", FakeCarHost)
public class FakeNavigationTests
{
    private static readonly CarMapTemplate _map = new() { Id = "map", TrailingBarButtons = [new CarBarButton("Destination") { Id = "dest" }] };

    private static async Task<FakeCarHost> Connected(CarPlatform platform, RecordingNavigationHandler? handler = null)
    {
        var host = new FakeCarHost(CarAppCategory.Navigation, platform) { NavigationHandler = handler };
        await host.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(_map)));
        return host;
    }

    [Fact]
    public async Task Fake_navigation_records_calls_and_simulates_the_driver()
    {
        var handler = new RecordingNavigationHandler();
        using var host = await Connected(CarPlatform.CarPlay, handler);
        var nav = host.Navigation;
        var trip = N.Trip();
        handler.OnStart = () => nav.StartTripAsync(trip, trip.Routes[0]);

        await nav.ShowTripPreviewsAsync([trip], trip);
        await host.SelectTripPreviewAsync(trip, trip.Routes[1]);
        await host.StartTripFromCarAsync(trip, trip.Routes[0]);
        await nav.UpdateEstimatesAsync(N.Estimates(8, 12), N.Estimates(0.3, 1));
        await host.SelectManeuverAsync(trip.Routes[0].Maneuvers[0]);
        await nav.PauseAsync(CarNavigationPauseReason.Arrived, "At the destination");
        await host.EndNavigationFromCarAsync();

        Assert.Equal(
            [
                FakeCarNavigationOperation.Restore,
                FakeCarNavigationOperation.ShowTripPreviews,
                FakeCarNavigationOperation.StartTrip,
                FakeCarNavigationOperation.UpdateEstimates,
                FakeCarNavigationOperation.Pause,
                FakeCarNavigationOperation.EndedByCar,
            ],
            host.NavigationCalls.Select(c => c.Operation));
        var preview = host.NavigationCalls[1];
        Assert.Same(trip, Assert.Single(preview.Trips));
        Assert.Same(trip, preview.Trip);
        var start = host.NavigationCalls[2];
        Assert.Same(trip.Routes[0], start.Route);
        Assert.Equal(["m1", "m2"], start.Maneuvers.Select(m => m.Id));
        Assert.Equal(N.Estimates(0.3, 1), host.NavigationCalls[3].ManeuverEstimates);
        Assert.Equal(CarNavigationPauseReason.Arrived, host.NavigationCalls[4].PauseReason);
        Assert.Equal("At the destination", host.NavigationCalls[4].PauseDescription?.Text);
        Assert.All(host.NavigationCalls, call => Assert.True(call.OnMainThread));
        Assert.Equal(["preview:trip/eco", "start:trip/fast", "maneuver:m1", "ended"], handler.Calls);
        Assert.Equal(CarNavigationState.Idle, nav.State);
    }

    [Fact]
    public async Task Fake_navigation_keeps_state_across_reconnects()
    {
        using var host = new FakeCarHost(CarAppCategory.Navigation);
        var trip = N.Trip();
        await host.Navigation.StartTripAsync(trip, trip.Routes[0]);
        Assert.Empty(host.NavigationCalls);

        await host.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(_map)));
        var restore = Assert.Single(host.NavigationCalls);
        Assert.Equal(FakeCarNavigationOperation.Restore, restore.Operation);
        Assert.Equal(CarNavigationState.Active, restore.State);
        Assert.Same(trip, restore.Trip);

        await host.DisconnectAsync();
        await host.Navigation.FinishAsync();
        Assert.Single(host.NavigationCalls);
    }

    [Fact]
    public async Task Fake_carplay_only_and_android_only_simulations_throw()
    {
        using var carPlay = await Connected(CarPlatform.CarPlay);
        using var androidAuto = await Connected(CarPlatform.AndroidAuto);
        var trip = N.Trip();

        await Assert.ThrowsAsync<InvalidOperationException>(() => carPlay.RequestNavigationAsync("geo:1,2"));
        await Assert.ThrowsAsync<InvalidOperationException>(carPlay.EnableAutoDriveAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => androidAuto.SelectManeuverAsync(N.Maneuver("m1")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => androidAuto.SelectNavigationAlertAsync(N.Alert()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => carPlay.SelectTripPreviewAsync(trip, trip.Routes[0]));
        using var lone = new FakeCarHost(CarAppCategory.Navigation);
        await Assert.ThrowsAsync<InvalidOperationException>(lone.EndNavigationFromCarAsync);
    }

    [Fact]
    public async Task Fake_android_auto_requests_and_auto_drive_reach_the_handler()
    {
        var handler = new RecordingNavigationHandler();
        using var host = await Connected(CarPlatform.AndroidAuto, handler);

        await host.RequestNavigationAsync("google.navigation:q=Main+Square+1");
        await host.EnableAutoDriveAsync();

        Assert.Equal(["request:Main Square 1", "autodrive"], handler.Calls);
    }

    [Fact]
    public async Task Fake_alert_action_dismisses_before_the_handler()
    {
        using var host = await Connected(CarPlatform.CarPlay);
        CarNavigationAlert? alertSeen = null;
        var alert = N.Alert(onSelected: _ =>
        {
            alertSeen = ((CarNavigationController)host.Navigation).Snapshot.Alert;
            return Task.CompletedTask;
        });
        await host.Navigation.ShowAlertAsync(alert);
        Assert.Equal(FakeCarNavigationOperation.ShowAlert, host.NavigationCalls[^1].Operation);
        Assert.Same(alert, host.NavigationCalls[^1].Alert);

        await host.SelectAsync(alert.PrimaryAction);

        Assert.Null(alertSeen);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectAsync(alert.SecondaryAction!));
    }

    [Fact]
    public async Task Fake_map_button_runs_its_handler()
    {
        var pressed = 0;
        var map = _map with { MapButtons = [new CarMapButton(CarImage.FromSystemSymbol("location", "ic_location")) { Id = "center", OnSelected = _ => { pressed++; return Task.CompletedTask; } }] };
        using var host = new FakeCarHost(CarAppCategory.Navigation, CarPlatform.AndroidAuto);
        await host.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(map)));

        await host.SelectAsync(map.MapButtons[0]);

        Assert.Equal(1, pressed);
    }
}
