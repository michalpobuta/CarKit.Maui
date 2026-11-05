using CarKit.Maui;

namespace CarKit.Sample.Navigation;

// Simulated sample routes (Springfield): a destination → a trip with two routes, maneuvers with icons, lanes and a
// roundabout. Stage 9: the "via the city center" route has a stop at a charger (two segments with path points for route
// sharing, CarPlay iOS 26.4)
public static class SampleRoutes
{
    public static readonly CarLocation Start = new(52.2319, 21.0067);

    public static readonly CarWaypoint Charger = new(new CarLocation(52.2065, 21.0101)) { Name = "City Center EV Charger", Address = "2 South Avenue\nSpringfield" };

    public static IReadOnlyList<SampleDestination> Destinations { get; } =
    [
        new("work", "Work", "39 Business Park Road", new CarLocation(52.1806, 21.0019)),
        new("airport", "City Airport", "1 Airport Road", new CarLocation(52.1657, 20.9671)),
    ];

    public static CarImage Icon(CarManeuverKind kind) => kind switch
    {
        CarManeuverKind.Right => CarImage.FromSystemSymbol("arrow.turn.up.right", "ic_nav_right"),
        CarManeuverKind.Left => CarImage.FromSystemSymbol("arrow.turn.up.left", "ic_nav_left"),
        CarManeuverKind.Roundabout => CarImage.FromSystemSymbol("arrow.triangle.turn.up.right.circle", "ic_nav_roundabout"),
        CarManeuverKind.Destination => CarImage.FromSystemSymbol("flag.checkered", "ic_nav_flag"),
        _ => CarImage.FromSystemSymbol("arrow.up", "ic_nav_straight"),
    };

    public static SampleDestination? Find(string? query) =>
        query is null ? null : Destinations.FirstOrDefault(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || query.Contains(d.Name, StringComparison.OrdinalIgnoreCase));

    public static CarTrip Trip(SampleDestination destination) => new(Start, destination.Location, [Fast(destination), Eco(destination)])
    {
        Id = "trip-" + destination.Id,
        DestinationName = destination.Name,
        DestinationAddress = destination.Address,
        // Stage 9: the destination can be sent to the car's built-in navigation (CarPlay iOS 26.1+, 26.4 asset)
        IsDestinationShareable = true,
    };

    // A detour around a traffic jam: different maneuvers of the same trip
    public static CarRouteChoice Detour(SampleDestination destination) => new("Detour via Elm Street")
    {
        Id = "detour-" + destination.Id,
        AdditionalInformation = "Avoids the traffic jam",
        Estimates = Estimates(6.8, 14),
        Maneuvers =
        [
            Maneuver("d1", CarManeuverKind.Left, "Turn left onto Elm Street", "Left", 0.4),
            Maneuver("d2", CarManeuverKind.Right, "Turn right onto Oak Street", "Right", 1.1),
            Maneuver("d3", CarManeuverKind.Destination, "Destination: " + destination.Name, destination.Name, 0.6),
        ],
    };

    public static CarTravelEstimates Estimates(double kilometers, double minutes) =>
        new(kilometers < 1 ? new CarDistance(Math.Round(kilometers * 1000), CarDistanceUnit.Meters) : new CarDistance(Math.Round(kilometers, 1), CarDistanceUnit.Kilometers),
            TimeSpan.FromMinutes(minutes));

    private static CarRouteChoice Fast(SampleDestination destination) => new("Via River Road")
    {
        Id = "fast-" + destination.Id,
        AdditionalInformation = "Fastest",
        SelectionSummary = "Fastest route",
        Estimates = Estimates(8.2, 16) with { TimeRemainingColor = CarTravelEstimatesColor.Green },
        Maneuvers =
        [
            Maneuver("f1", CarManeuverKind.Straight, "Continue straight on Main Street", "Straight", 0.6),
            Maneuver("f2", CarManeuverKind.Right, "Turn right onto Central Avenue", "Right", 0.3) with
            {
                Road = "Central Avenue",
                Lanes =
                [
                    new CarLane([new CarLaneDirection(CarLaneShape.Straight, false)]),
                    new CarLane([new CarLaneDirection(CarLaneShape.Straight, false), new CarLaneDirection(CarLaneShape.Right, true)]),
                    new CarLane([new CarLaneDirection(CarLaneShape.Right, true)]),
                ],
                LanesImage = CarImage.FromSystemSymbol("road.lanes", "ic_nav_lanes"),
            },
            Maneuver("f3", CarManeuverKind.Roundabout, "At the roundabout take the third exit", "Roundabout, 3rd exit", 1.2) with { RoundaboutExitNumber = 3 },
            Maneuver("f4", CarManeuverKind.Left, "Turn left onto Park Street", "Left", 2.1),
            Maneuver("f5", CarManeuverKind.Destination, "Destination: " + destination.Name, destination.Name, 0.8),
        ],
    };

    private static CarRouteChoice Eco(SampleDestination destination)
    {
        CarManeuver[] maneuvers =
        [
            Maneuver("e1", CarManeuverKind.Left, "Turn left onto Cross Street", "Left", 0.5),
            Maneuver("e2", CarManeuverKind.Right, "Turn right onto South Avenue and stop at the charger", "Charger", 2.4),
            Maneuver("e3", CarManeuverKind.Destination, "Destination: " + destination.Name, destination.Name, 1.1),
        ];
        var target = new CarWaypoint(destination.Location) { Name = destination.Name, Address = destination.Address };
        return new("Via the city center with a stop")
        {
            Id = "eco-" + destination.Id,
            AdditionalInformation = "Stop at an EV charger",
            Estimates = Estimates(7.1, 21) with { TimeRemainingColor = CarTravelEstimatesColor.Orange },
            Maneuvers = [.. maneuvers],
            Segments =
            [
                new CarRouteSegment(new CarWaypoint(Start) { Name = "Start" }, Charger)
                {
                    Path = [Start, new CarLocation(52.2297, 21.0050), new CarLocation(52.2180, 21.0123), Charger.Location],
                    Maneuvers = [maneuvers[0], maneuvers[1]],
                    Estimates = Estimates(2.9, 9),
                },
                new CarRouteSegment(Charger, target)
                {
                    Path = [Charger.Location, new CarLocation(52.1950, 21.0080), destination.Location],
                    Maneuvers = [maneuvers[2]],
                    Estimates = Estimates(4.2, 12),
                },
            ],
        };
    }

    private static CarManeuver Maneuver(string id, CarManeuverKind kind, string instruction, string shortInstruction, double kilometers) =>
        new(new CarText([instruction, shortInstruction]))
        {
            Id = id,
            Kind = kind,
            Icon = Icon(kind),
            InitialEstimates = Estimates(kilometers, Math.Max(kilometers * 2, 0.5)),
        };
}

public sealed record SampleDestination(string Id, string Name, string Address, CarLocation Location);
