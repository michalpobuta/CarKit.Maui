using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Navigation;

// Stage 9: route waypoints, segments, detour reason and car requests
// (spec: "New platform features decisions (Stage 9)", Navigation (iOS 26.4))
public class NavigationSegmentTests
{
    private static readonly CarWaypoint _charger = new(new CarLocation(52.20, 21.00)) { Name = "Charger", Address = "Main St. 1\nWarsaw" };

    private static CarRouteChoice TwoLegs(string id = "stop") => new("Via charger")
    {
        Id = id,
        Estimates = N.Estimates(12, 25),
        Maneuvers = [N.Maneuver("m1")],
        Segments =
        [
            new CarRouteSegment(new CarWaypoint(N.Home), _charger) { Path = [N.Home, _charger.Location], Maneuvers = [N.Maneuver("m1")], Estimates = N.Estimates(5, 10) },
            new CarRouteSegment(_charger, new CarWaypoint(N.Work) { Name = "Work" }) { Path = [_charger.Location, N.Work], Maneuvers = [N.Maneuver("m3")], Estimates = N.Estimates(7, 15) },
        ],
    };

    private static CarTrip Trip(CarRouteChoice? route = null) => new(N.Home, N.Work, [route ?? TwoLegs()]) { Id = "trip", DestinationName = "Work", DestinationAddress = "Office St. 2" };

    [Fact]
    public async Task Navigation_segment_index_starts_at_zero_and_moves()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var trip = Trip();

        await f.Controller.StartTripAsync(trip, trip.Routes[0]);
        Assert.Equal(0, f.Controller.CurrentSegmentIndex);

        await f.Controller.UpdateCurrentSegmentAsync(1);

        Assert.Equal(1, f.Controller.CurrentSegmentIndex);
        Assert.Equal(1, Assert.IsType<CarNavigationChange.SegmentChanged>(f.Sink.Changes[^1]).Index);
        Assert.Equal(1, f.Sink.States[^1].SegmentIndex);
    }

    [Fact]
    public async Task Navigation_segment_index_outside_the_route_throws()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var trip = Trip();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Controller.UpdateCurrentSegmentAsync(0));
        await f.Controller.StartTripAsync(trip, trip.Routes[0]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Controller.UpdateCurrentSegmentAsync(2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Controller.UpdateCurrentSegmentAsync(-1));
        Assert.Equal(0, f.Controller.CurrentSegmentIndex);
    }

    [Fact]
    public async Task Navigation_reroute_records_the_reason()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var trip = Trip();
        await f.Controller.StartTripAsync(trip, trip.Routes[0]);
        await f.Controller.UpdateCurrentSegmentAsync(1);

        await f.Controller.RerouteAsync(TwoLegs("detour"), CarRerouteReason.MissedTurn);

        var change = Assert.IsType<CarNavigationChange.Rerouted>(f.Sink.Changes[^1]);
        Assert.Equal(CarRerouteReason.MissedTurn, change.Reason);
        Assert.Equal(CarRerouteReason.MissedTurn, f.Sink.States[^1].RerouteReason);
        // A new route starts from the first segment
        Assert.Equal(0, f.Controller.CurrentSegmentIndex);
        await f.Controller.RerouteAsync(TwoLegs("again"));
        Assert.Equal(CarRerouteReason.Unknown, Assert.IsType<CarNavigationChange.Rerouted>(f.Sink.Changes[^1]).Reason);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Controller.RerouteAsync(TwoLegs("bad"), (CarRerouteReason)42));
    }

    // Review (I2): resuming after a pause without a detour does not give the car the reason for the previous detour
    [Fact]
    public async Task Navigation_resume_after_pause_has_no_reroute_reason()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var trip = Trip();
        await f.Controller.StartTripAsync(trip, trip.Routes[0]);
        await f.Controller.RerouteAsync(TwoLegs("detour"), CarRerouteReason.AlternateRoute);
        await f.Controller.PauseAsync(CarNavigationPauseReason.Locating);

        await f.Controller.UpdateManeuversAsync([N.Maneuver("m1")]);

        Assert.True(Assert.IsType<CarNavigationChange.ManeuversUpdated>(f.Sink.Changes[^1]).Resumed);
        Assert.Null(f.Sink.States[^1].RerouteReason);
    }

    [Fact]
    public void Navigation_segments_default_to_one_leg()
    {
        var trip = N.Trip();

        var legs = CarNavigationSegments.Legs(trip, trip.Routes[0]);

        var leg = Assert.Single(legs);
        Assert.Equal(N.Home, leg.Origin.Location);
        Assert.Equal(N.Work, leg.Destination.Location);
        Assert.Equal("Work", leg.Destination.Name?.Text);
        Assert.Equal(["m1", "m2"], leg.Maneuvers.Select(m => m.Id));
        Assert.Equal(trip.Routes[0].Estimates, leg.Estimates);
        Assert.Equal(2, CarNavigationSegments.Legs(Trip(), TwoLegs()).Count);
    }

    [Fact]
    public void Trip_destinations_follow_the_remaining_segments()
    {
        var trip = Trip();
        var live = N.Estimates(11, 20);

        var all = CarNavigationSegments.RemainingStops(trip, trip.Routes[0], segmentIndex: 0, tripEstimates: live);
        var last = CarNavigationSegments.RemainingStops(trip, trip.Routes[0], segmentIndex: 1, tripEstimates: live);

        Assert.Equal(["Charger", "Work"], all.Select(stop => stop.Waypoint.Name?.Text));
        Assert.Equal(N.Estimates(5, 10), all[0].Estimates);
        // The route destination gets the session's current estimate
        Assert.Equal(live, all[1].Estimates);
        Assert.Equal(["Work"], last.Select(stop => stop.Waypoint.Name?.Text));
    }

    [Fact]
    public void Segment_without_estimates_is_skipped_on_android_auto()
    {
        var route = TwoLegs() with { Segments = [TwoLegs().Segments[0] with { Estimates = null }, TwoLegs().Segments[1]] };
        var trip = Trip(route);

        var stops = CarNavigationSegments.RemainingStops(trip, route, 0, tripEstimates: null);

        Assert.Equal(["Work"], stops.Select(stop => stop.Waypoint.Name?.Text));
        Assert.Equal(N.Estimates(7, 15), stops[0].Estimates);
        Assert.Empty(CarNavigationSegments.RemainingStops(N.Trip(), new CarRouteChoice("No estimate") { Id = "none" }, 0, tripEstimates: null));
    }

    [Fact]
    public async Task Waypoint_insert_returns_the_handler_estimates()
    {
        var handler = new SharingHandler { Estimates = N.Estimates(2, 4) };
        using var host = await ConnectedAsync(CarPlatform.CarPlay, handler);

        var estimates = await host.RequestWaypointInsertAsync(_charger, segmentIndex: 1);

        Assert.Equal(N.Estimates(2, 4), estimates);
        Assert.Equal(["insert:Charger@1"], handler.Calls);
    }

    [Fact]
    public async Task Waypoint_insert_without_handler_completes()
    {
        using var host = await ConnectedAsync(CarPlatform.CarPlay, handler: null);
        Assert.Null(await host.RequestWaypointInsertAsync(_charger, 0));

        using var failing = await ConnectedAsync(CarPlatform.CarPlay, new SharingHandler { Fail = true });
        Assert.Null(await failing.RequestWaypointInsertAsync(_charger, 0));
        Assert.Contains(failing.Errors, e => e is InvalidOperationException);
    }

    [Fact]
    public async Task Waypoint_requests_only_on_carplay_with_route_sharing()
    {
        using var androidAuto = await ConnectedAsync(CarPlatform.AndroidAuto, new SharingHandler());
        await Assert.ThrowsAsync<InvalidOperationException>(() => androidAuto.RequestWaypointInsertAsync(_charger, 0));

        using var older = new FakeCarHost(CarAppCategory.Navigation) { NavigationHandler = new SharingHandler() };
        older.Features.Remove(CarFeature.RouteSharing);
        await older.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(_map)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => older.RequestDestinationAsync(_charger));

        using var idle = new FakeCarHost(CarAppCategory.Navigation) { NavigationHandler = new SharingHandler() };
        await idle.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(_map)));
        // The stop request concerns the route being navigated
        await Assert.ThrowsAsync<InvalidOperationException>(() => idle.RequestWaypointInsertAsync(_charger, 0));
    }

    [Fact]
    public async Task Route_source_and_destination_requests_reach_the_handler()
    {
        var handler = new SharingHandler();
        using var host = await ConnectedAsync(CarPlatform.CarPlay, handler);

        await host.ChangeRouteSourceAsync(CarRouteSource.Vehicle);
        await host.RequestDestinationAsync(_charger);
        await host.AnswerWaypointProposalAsync(_charger, accepted: true, segmentIndex: 0);

        Assert.Equal(["source:Vehicle", "destination:Charger", "answer:Charger:True@0"], handler.Calls);
        Assert.Contains(host.NavigationCalls, call => call.Operation == FakeCarNavigationOperation.StartTrip && call.SegmentIndex == 0);
    }

    [Fact]
    public async Task Fake_host_records_the_segment_and_the_reroute_reason()
    {
        using var host = await ConnectedAsync(CarPlatform.CarPlay, new SharingHandler());

        await host.Navigation.UpdateCurrentSegmentAsync(1);
        await host.Navigation.RerouteAsync(TwoLegs("detour"), CarRerouteReason.WaypointModified);

        var segment = host.NavigationCalls[^2];
        Assert.Equal(FakeCarNavigationOperation.UpdateSegment, segment.Operation);
        Assert.Equal(1, segment.SegmentIndex);
        var reroute = host.NavigationCalls[^1];
        Assert.Equal(CarRerouteReason.WaypointModified, reroute.RerouteReason);
        Assert.Equal(0, reroute.SegmentIndex);
    }

    private static readonly CarMapTemplate _map = new() { Id = "map", TrailingBarButtons = [new CarBarButton("Destination") { Id = "dest" }] };

    private static async Task<FakeCarHost> ConnectedAsync(CarPlatform platform, ICarNavigationHandler? handler)
    {
        var host = new FakeCarHost(CarAppCategory.Navigation, platform) { NavigationHandler = handler };
        await host.ConnectAsync(new Session.TestHandler((s, _) => s.SetRootAsync(_map)));
        var trip = Trip();
        await host.Navigation.StartTripAsync(trip, trip.Routes[0]);
        return host;
    }

    private sealed class SharingHandler : ICarNavigationHandler
    {
        public List<string> Calls { get; } = [];

        public CarTravelEstimates? Estimates { get; init; }

        public bool Fail { get; init; }

        public Task<CarTravelEstimates?> OnWaypointInsertRequestedAsync(CarWaypoint waypoint, int segmentIndex)
        {
            if (Fail)
            {
                throw new InvalidOperationException("No route through this point.");
            }

            Calls.Add($"insert:{waypoint.Name?.Text}@{segmentIndex}");
            return Task.FromResult(Estimates);
        }

        public Task OnWaypointProposalAnsweredAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex)
        {
            Calls.Add($"answer:{waypoint.Name?.Text}:{accepted}@{segmentIndex}");
            return Task.CompletedTask;
        }

        public Task OnDestinationRequestedAsync(CarWaypoint destination)
        {
            Calls.Add($"destination:{destination.Name?.Text}");
            return Task.CompletedTask;
        }

        public Task OnRouteSourceChangedAsync(CarRouteSource source)
        {
            Calls.Add($"source:{source}");
            return Task.CompletedTask;
        }
    }
}
