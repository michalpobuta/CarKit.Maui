using CarKit.Maui.Testing;
using CarKit.Sample.Navigation;
using Xunit;

namespace CarKit.Maui.Tests.Sample;

// Stage 8: the simulated sample route from preview to finish on FakeCarHost (CarPlay and Android Auto, Strict); the
// acceptance "the simulated route goes from preview to finish" at the session level; in CarPlay Simulator and DHU:
// manual-verification
public class NavigationSampleTests
{
    private static async Task<(FakeCarHost Host, SampleRouteSimulator Simulator)> Connect(CarPlatform platform)
    {
        var host = new FakeCarHost(CarAppCategory.Navigation, platform);
        host.Options.Validation = CarValidationMode.Strict;
        var simulator = new SampleRouteSimulator(host.Navigation, voice: null, delay: (_, ct) => Task.Delay(1, ct));
        host.NavigationHandler = new SampleNavigationHandler(simulator);
        await host.ConnectAsync(new SampleNavigationCarHandler(simulator, new SampleMapState()));
        return (host, simulator);
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public async Task Simulated_route_goes_from_preview_to_finish(CarPlatform platform)
    {
        var (host, simulator) = await Connect(platform);
        using var _ = host;
        var map = Assert.IsType<CarMapTemplate>(Assert.Single(host.Session!.Stack));

        await host.SelectAsync(map.TrailingBarButtons[0]);
        var destinations = Assert.IsType<CarListTemplate>(host.Session.Stack[^1]);
        await host.SelectAsync(destinations.Sections[0].Items[0]);
        Assert.Equal(CarNavigationState.Previewing, host.Navigation.State);
        Assert.Single(host.Session.Stack);
        var trip = host.Navigation.TripPreviews[0];
        await host.SelectTripPreviewAsync(trip, trip.Routes[1]);
        await host.StartTripFromCarAsync(trip, trip.Routes[0]);
        await simulator.Running!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var operations = host.NavigationCalls.Select(c => c.Operation).ToList();
        Assert.Equal(CarNavigationState.Idle, host.Navigation.State);
        Assert.Equal(FakeCarNavigationOperation.Finish, operations[^1]);
        Assert.Equal(
            [
                FakeCarNavigationOperation.Restore,
                FakeCarNavigationOperation.ShowTripPreviews,
                FakeCarNavigationOperation.StartTrip,
                FakeCarNavigationOperation.UpdateEstimates,
                FakeCarNavigationOperation.UpdateManeuvers,
                FakeCarNavigationOperation.ShowAlert,
                FakeCarNavigationOperation.Pause,
                FakeCarNavigationOperation.Reroute,
                FakeCarNavigationOperation.Finish,
            ],
            operations.Distinct());
        var arrived = host.NavigationCalls.Last(c => c.Operation == FakeCarNavigationOperation.Pause);
        Assert.Equal(CarNavigationPauseReason.Arrived, arrived.PauseReason);
        var reroute = host.NavigationCalls.Single(c => c.Operation == FakeCarNavigationOperation.Reroute);
        Assert.StartsWith("detour-", reroute.Route!.Id, StringComparison.Ordinal);
        Assert.True(operations.Count(o => o == FakeCarNavigationOperation.UpdateEstimates) >= 6);
        Assert.Equal(1, simulator.RouteProgress);
        Assert.Empty(host.Errors);
    }

    // Stage 9: the "via the city center" route has a stop at a charger: two segments, UpdateCurrentSegmentAsync(1)
    // after the stop; the detour of the fast route gives a reason (AlternateRoute); car requests (route sharing,
    // CarPlay iOS 26.4) reach the handler
    [Fact]
    public async Task Route_with_a_stop_has_two_segments()
    {
        var (host, simulator) = await Connect(CarPlatform.CarPlay);
        using var _ = host;
        var trip = SampleRoutes.Trip(SampleRoutes.Destinations[0]);
        var stop = trip.Routes[1];

        Assert.Equal(2, stop.Segments.Count);
        Assert.Equal("City Center EV Charger", stop.Segments[0].Destination.Name?.Text);
        Assert.Equal(stop.Segments[0].Destination, stop.Segments[1].Origin);
        Assert.False(stop.Segments[1].Path.IsEmpty);
        Assert.True(trip.IsDestinationShareable);
        await host.Navigation.ShowTripPreviewsAsync([trip], trip);
        await host.StartTripFromCarAsync(trip, stop);
        await simulator.Running!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var segment = Assert.Single(host.NavigationCalls, c => c.Operation == FakeCarNavigationOperation.UpdateSegment);
        Assert.Equal(1, segment.SegmentIndex);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Detour_reports_the_reason_and_car_requests_reach_the_sample()
    {
        var (host, simulator) = await Connect(CarPlatform.CarPlay);
        using var _ = host;
        var progress = new List<string>();
        simulator.Progress += (_, message) => progress.Add(message);
        await simulator.PreviewAsync(SampleRoutes.Destinations[0]);
        var trip = host.Navigation.TripPreviews[0];
        await host.StartTripFromCarAsync(trip, trip.Routes[0]);

        var estimates = await host.RequestWaypointInsertAsync(new CarWaypoint(new CarLocation(52.20, 21.0)) { Name = "Charger" }, 0);
        await host.ChangeRouteSourceAsync(CarRouteSource.PhoneUnchanged);
        await simulator.Running!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.NotNull(estimates);
        Assert.Equal(CarRerouteReason.AlternateRoute, host.NavigationCalls.Single(c => c.Operation == FakeCarNavigationOperation.Reroute).RerouteReason);
        Assert.Contains(progress, message => message.StartsWith("car: stop Charger", StringComparison.Ordinal));
        Assert.Contains("car: route source PhoneUnchanged", progress);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Car_end_stops_the_simulation()
    {
        var (host, simulator) = await Connect(CarPlatform.CarPlay);
        using var _ = host;
        simulator.Step = TimeSpan.FromSeconds(1);
        var slow = new SampleRouteSimulator(host.Navigation, null, Task.Delay);
        host.NavigationHandler = new SampleNavigationHandler(slow);
        await slow.PreviewAsync(SampleRoutes.Destinations[1]);
        var trip = host.Navigation.TripPreviews[0];

        await host.StartTripFromCarAsync(trip, trip.Routes[1]);
        await host.EndNavigationFromCarAsync();
        await slow.Running!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(CarNavigationState.Idle, host.Navigation.State);
        Assert.Equal(FakeCarNavigationOperation.EndedByCar, host.NavigationCalls[^1].Operation);
        Assert.DoesNotContain(host.NavigationCalls, c => c.Operation == FakeCarNavigationOperation.Finish);
    }

    [Fact]
    public async Task Auto_drive_and_navigation_request_start_the_trip()
    {
        var (host, simulator) = await Connect(CarPlatform.AndroidAuto);
        using var _ = host;

        await host.EnableAutoDriveAsync();
        await host.RequestNavigationAsync("geo:0,0?q=City+Airport");
        Assert.Equal("City Airport", simulator.Destination?.Name);
        await simulator.Running!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Contains(host.NavigationCalls, c => c.Operation == FakeCarNavigationOperation.StartTrip && c.Route!.Id == "fast-airport");
        Assert.Equal(FakeCarNavigationOperation.Finish, host.NavigationCalls[^1].Operation);
        await host.RequestNavigationAsync("geo:0,0?q=Unknown");
        Assert.Equal(CarNavigationState.Idle, host.Navigation.State);
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public async Task Map_buttons_change_the_map_state(CarPlatform platform)
    {
        var host = new FakeCarHost(CarAppCategory.Navigation, platform);
        using var _ = host;
        var map = new SampleMapState();
        var simulator = new SampleRouteSimulator(host.Navigation);
        await host.ConnectAsync(new SampleNavigationCarHandler(simulator, map));
        var root = Assert.IsType<CarMapTemplate>(host.Session!.Stack[0]);

        await host.SelectAsync(root.MapButtons[1]);
        Assert.Equal(1.5f, map.Zoom, 3);
        await host.SelectAsync(root.MapButtons[0]);
        Assert.Equal(1f, map.Zoom);
        Assert.Empty(host.Errors);
    }
}
