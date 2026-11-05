using Xunit;

namespace CarKit.Maui.Core.Tests.Navigation;

// Stage 8: navigation model (spec: "Navigation module decisions (Stage 8)", Model)
internal static class N
{
    public static readonly CarLocation Home = new(52.2297, 21.0122);
    public static readonly CarLocation Work = new(52.1672, 20.9679);

    public static CarTravelEstimates Estimates(double kilometers, int minutes) =>
        new(new CarDistance(kilometers, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(minutes));

    public static CarManeuver Maneuver(string id, CarManeuverKind kind = CarManeuverKind.Right) =>
        new("Turn right at " + id) { Id = id, Kind = kind, InitialEstimates = Estimates(0.5, 1) };

    public static CarRouteChoice Route(string id, params CarManeuver[] maneuvers) =>
        new("Route " + id) { Id = id, AdditionalInformation = "Via downtown", Estimates = Estimates(12, 25), Maneuvers = [.. maneuvers] };

    public static CarTrip Trip(string id = "trip", params CarRouteChoice[] routes) =>
        new(Home, Work, routes.Length > 0 ? routes : [Route("fast", Maneuver("m1"), Maneuver("m2")), Route("eco")]) { Id = id, DestinationName = "Work" };

    public static CarNavigationAlert Alert(string id = "alert", Func<CarItemContext, Task>? onSelected = null) =>
        new("Traffic jam in 2 km", new CarAction("Detour", onSelected) { Id = id + "-primary" }) { Id = id, SecondaryAction = new CarAction("Stay") { Id = id + "-secondary" } };
}

public class NavigationModelTests
{
    [Fact]
    public void Trip_needs_a_route()
    {
        Assert.Throws<ArgumentException>(() => new CarTrip(N.Home, N.Work, []));
        Assert.Throws<ArgumentNullException>(() => new CarTrip(N.Home, N.Work, null!));
        var trip = N.Trip();
        Assert.Equal(2, trip.Routes.Count);
        Assert.Equal(N.Work, trip.Destination);
        Assert.Equal(N.Home, trip.Origin);
    }

    [Fact]
    public void Trip_routes_need_their_own_ids() =>
        Assert.Throws<ArgumentException>(() => new CarTrip(N.Home, N.Work, [N.Route("a"), N.Route("a")]));

    [Fact]
    public void Estimates_reject_negative_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarTravelEstimates(new CarDistance(1, CarDistanceUnit.Meters), TimeSpan.FromSeconds(-1)));
        var estimates = N.Estimates(2.5, 7);
        Assert.Equal(TimeSpan.FromMinutes(7), estimates.TimeRemaining);
        Assert.Equal(new CarDistance(2.5, CarDistanceUnit.Kilometers), estimates.DistanceRemaining);
        Assert.Null(estimates.ArrivalTime);
        Assert.Equal(CarTravelEstimatesColor.Default, estimates.TimeRemainingColor);
        Assert.Equal(estimates, N.Estimates(2.5, 7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Roundabout_exit_number_range(int exit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => N.Maneuver("r", CarManeuverKind.Roundabout) with { RoundaboutExitNumber = exit });

    [Theory]
    [InlineData(0)]
    [InlineData(361)]
    public void Roundabout_exit_angle_range(int angle) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => N.Maneuver("r", CarManeuverKind.Roundabout) with { RoundaboutExitAngle = angle });

    [Fact]
    public void Maneuver_needs_an_instruction()
    {
        Assert.Throws<ArgumentNullException>(() => new CarManeuver(null!));
        var maneuver = N.Maneuver("m") with { RoundaboutExitNumber = 19, RoundaboutExitAngle = 360, TrafficSide = CarTrafficSide.Left };
        Assert.Equal(19, maneuver.RoundaboutExitNumber);
        Assert.Equal(CarTrafficSide.Left, maneuver.TrafficSide);
        Assert.Equal(CarTrafficSide.Right, N.Maneuver("x").TrafficSide);
    }

    [Fact]
    public void Lane_needs_a_direction()
    {
        Assert.Throws<ArgumentException>(() => new CarLane([]));
        var lane = new CarLane([new CarLaneDirection(CarLaneShape.Straight, IsRecommended: true), new CarLaneDirection(CarLaneShape.Right, false)]);
        Assert.Equal(2, lane.Directions.Count);
        Assert.Equal(lane, new CarLane([new CarLaneDirection(CarLaneShape.Straight, true), new CarLaneDirection(CarLaneShape.Right, false)]));
    }

    [Fact]
    public void Alert_duration_must_be_positive()
    {
        var alert = N.Alert();
        Assert.Equal(TimeSpan.FromSeconds(10), alert.Duration);
        Assert.Throws<ArgumentOutOfRangeException>(() => alert with { Duration = TimeSpan.Zero });
        Assert.Throws<ArgumentNullException>(() => new CarNavigationAlert("Title", null!));
    }

    [Fact]
    public void Map_template_defaults()
    {
        var map = new CarMapTemplate();
        Assert.False(map.IsPanningEnabled);
        Assert.Null(map.StartTripTitle);
        Assert.True(map.AutomaticallyHidesNavigationBar);
    }

    [Theory]
    [InlineData("geo:52.2297,21.0122", 52.2297, 21.0122, null)]
    [InlineData("geo:52.2297,21.0122?z=15", 52.2297, 21.0122, null)]
    [InlineData("geo:0,0?q=52.1672,20.9679(Work)", 52.1672, 20.9679, "Work")]
    [InlineData("geo:0,0?q=Caf%C3%A9+Z%C3%BCrich+10,+Wellington", null, null, "Café Zürich 10, Wellington")]
    [InlineData("google.navigation:q=52.1,21.2", 52.1, 21.2, null)]
    [InlineData("google.navigation:q=Main+Square+1", null, null, "Main Square 1")]
    [InlineData("https://example.com/route", null, null, null)]
    public void Navigation_request_parses_geo_and_google_navigation(string uri, double? latitude, double? longitude, string? query)
    {
        var request = new CarNavigationRequest(new Uri(uri));

        Assert.Equal(uri, request.Uri.OriginalString);
        Assert.Equal(query, request.Query);
        if (latitude is { } lat)
        {
            Assert.Equal(new CarLocation(lat, longitude!.Value), request.Location);
        }
        else
        {
            Assert.Null(request.Location);
        }
    }
}
