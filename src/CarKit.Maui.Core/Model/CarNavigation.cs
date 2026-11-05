namespace CarKit.Maui;

// Stage 8: turn-by-turn navigation model (spec: "Navigation module decisions (Stage 8)", Model). The Navigation module
// maps it to CPTrip/CPRouteChoice/CPManeuver/CPLaneGuidance/CPTravelEstimates/CPNavigationAlert (CarPlay) and
// Trip/Step/Maneuver/Lane/TravelEstimate/Alert (Car App Library); FakeCarHost and app tests use it without a platform.

/// <summary>The navigation state of <see cref="ICarNavigationSession"/>.</summary>
public enum CarNavigationState
{
    /// <summary>No trip: the map is shown without guidance.</summary>
    Idle,

    /// <summary>Trip previews are shown for the driver to pick a route.</summary>
    Previewing,

    /// <summary>Turn-by-turn guidance is active.</summary>
    Active,

    /// <summary>Guidance is paused, for example while rerouting or after arriving.</summary>
    Paused,
}

/// <summary>Why guidance is paused (CarPlay <c>CPTripPauseReason</c>).</summary>
public enum CarNavigationPauseReason
{
    /// <summary>The driver arrived at the destination.</summary>
    Arrived,

    /// <summary>The route is loading.</summary>
    Loading,

    /// <summary>The app is determining the car's location.</summary>
    Locating,

    /// <summary>The app is calculating a new route.</summary>
    Rerouting,

    /// <summary>The driver has to drive to the start of the route first.</summary>
    ProceedToRoute,
}

/// <summary>The kind of a maneuver; each platform shows its own symbol for it.</summary>
public enum CarManeuverKind
{
    /// <summary>Unknown or no turn.</summary>
    Unknown,

    /// <summary>Start of the route.</summary>
    Depart,

    /// <summary>Go straight.</summary>
    Straight,

    /// <summary>Slight left turn.</summary>
    SlightLeft,

    /// <summary>Left turn.</summary>
    Left,

    /// <summary>Sharp left turn.</summary>
    SharpLeft,

    /// <summary>Slight right turn.</summary>
    SlightRight,

    /// <summary>Right turn.</summary>
    Right,

    /// <summary>Sharp right turn.</summary>
    SharpRight,

    /// <summary>U-turn to the left.</summary>
    UTurnLeft,

    /// <summary>U-turn to the right.</summary>
    UTurnRight,

    /// <summary>Keep left.</summary>
    KeepLeft,

    /// <summary>Keep right.</summary>
    KeepRight,

    /// <summary>Merge to the left (CarPlay: keep left).</summary>
    MergeLeft,

    /// <summary>Merge to the right (CarPlay: keep right).</summary>
    MergeRight,

    /// <summary>On-ramp on the left.</summary>
    OnRampLeft,

    /// <summary>On-ramp on the right.</summary>
    OnRampRight,

    /// <summary>Off-ramp on the left.</summary>
    OffRampLeft,

    /// <summary>Off-ramp on the right.</summary>
    OffRampRight,

    /// <summary>Fork to the left (CarPlay: keep left).</summary>
    ForkLeft,

    /// <summary>Fork to the right (CarPlay: keep right).</summary>
    ForkRight,

    /// <summary>Enter a roundabout.</summary>
    RoundaboutEnter,

    /// <summary>Leave a roundabout.</summary>
    RoundaboutExit,

    /// <summary>Enter a roundabout and take an exit; needs <see cref="CarManeuver.RoundaboutExitNumber"/>.</summary>
    Roundabout,

    /// <summary>Board a ferry.</summary>
    Ferry,

    /// <summary>Arrive at the destination.</summary>
    Destination,

    /// <summary>Arrive at the destination on the left.</summary>
    DestinationLeft,

    /// <summary>Arrive at the destination on the right.</summary>
    DestinationRight,

    /// <summary>The road changes its name; keep following it.</summary>
    NameChange,
}

/// <summary>The side of the road the traffic drives on, which decides the roundabout direction.</summary>
public enum CarTrafficSide
{
    /// <summary>Right-hand traffic (counterclockwise roundabouts).</summary>
    Right,

    /// <summary>Left-hand traffic (clockwise roundabouts).</summary>
    Left,
}

/// <summary>The shape of one direction a lane allows.</summary>
public enum CarLaneShape
{
    /// <summary>Unknown.</summary>
    Unknown,

    /// <summary>Straight ahead.</summary>
    Straight,

    /// <summary>Slight left.</summary>
    SlightLeft,

    /// <summary>Left.</summary>
    Left,

    /// <summary>Sharp left.</summary>
    SharpLeft,

    /// <summary>U-turn to the left.</summary>
    UTurnLeft,

    /// <summary>Slight right.</summary>
    SlightRight,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Sharp right.</summary>
    SharpRight,

    /// <summary>U-turn to the right.</summary>
    UTurnRight,
}

/// <summary>One direction of a lane.</summary>
/// <param name="Shape">The direction shape.</param>
/// <param name="IsRecommended">Whether the driver should take the lane in this direction.</param>
public readonly record struct CarLaneDirection(CarLaneShape Shape, bool IsRecommended);

/// <summary>A lane with the directions it allows (CarPlay <c>CPLane</c>, iOS 17.4; Android Auto <c>Lane</c>).</summary>
public sealed record CarLane
{
    /// <summary>Creates the lane.</summary>
    /// <param name="directions">The directions; at least one.</param>
    public CarLane(IEnumerable<CarLaneDirection> directions)
    {
        var array = directions?.ToCarArray() ?? throw new ArgumentNullException(nameof(directions));
        if (array.IsEmpty)
        {
            throw new ArgumentException("A lane needs at least one direction.", nameof(directions));
        }

        Directions = array;
    }

    /// <summary>Gets the directions.</summary>
    public CarArray<CarLaneDirection> Directions { get; }
}

/// <summary>The color of the remaining time, for example to show traffic.</summary>
public enum CarTravelEstimatesColor
{
    /// <summary>The platform default.</summary>
    Default,

    /// <summary>Green (free flow).</summary>
    Green,

    /// <summary>Orange (Android Auto: yellow; moderate traffic).</summary>
    Orange,

    /// <summary>Red (heavy traffic).</summary>
    Red,
}

/// <summary>Remaining distance and time to a maneuver or to the destination.</summary>
public sealed record CarTravelEstimates
{
    /// <summary>Creates the estimates.</summary>
    /// <param name="distanceRemaining">The remaining distance as the car shows it.</param>
    /// <param name="timeRemaining">The remaining time; not negative.</param>
    public CarTravelEstimates(CarDistance distanceRemaining, TimeSpan timeRemaining)
    {
        if (timeRemaining < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeRemaining), timeRemaining, "Remaining time must not be negative.");
        }

        DistanceRemaining = distanceRemaining;
        TimeRemaining = timeRemaining;
    }

    /// <summary>Gets the remaining distance.</summary>
    public CarDistance DistanceRemaining { get; }

    /// <summary>Gets the remaining time.</summary>
    public TimeSpan TimeRemaining { get; }

    /// <summary>Gets the arrival time (Android Auto); when not set, the current time plus <see cref="TimeRemaining"/>.</summary>
    public DateTimeOffset? ArrivalTime { get; init; }

    /// <summary>Gets the color of the remaining time.</summary>
    public CarTravelEstimatesColor TimeRemainingColor { get; init; }
}

/// <summary>A maneuver of the route (CarPlay <c>CPManeuver</c>, Android Auto <c>Step</c> with <c>Maneuver</c>).</summary>
public sealed record CarManeuver : CarElement
{
    /// <summary>Creates the maneuver.</summary>
    /// <param name="instruction">The instruction, with shorter variants for small screens.</param>
    public CarManeuver(CarText instruction) => Instruction = instruction;

    /// <summary>Gets the instruction.</summary>
    public CarText Instruction { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the kind (CarPlay <c>ManeuverType</c> from iOS 17.4, Android Auto maneuver type).</summary>
    public CarManeuverKind Kind { get; init; }

    /// <summary>Gets the symbol (CarPlay <c>SymbolImage</c>, Android Auto maneuver icon).</summary>
    public CarImage? Icon { get; init; }

    /// <summary>Gets the road the maneuver leads to (Android Auto step road).</summary>
    public CarText? Road { get; init; }

    /// <summary>Gets the roundabout exit to take, from 1 to 19; required for <see cref="CarManeuverKind.Roundabout"/>.</summary>
    public int? RoundaboutExitNumber
    {
        get;
        init
        {
            if (value is < 1 or > 19)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Roundabout exit number must be from 1 to 19.");
            }

            field = value;
        }
    }

    /// <summary>Gets the roundabout exit angle in degrees, from 1 to 360 (Android Auto).</summary>
    public int? RoundaboutExitAngle
    {
        get;
        init
        {
            if (value is < 1 or > 360)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Roundabout exit angle must be from 1 to 360 degrees.");
            }

            field = value;
        }
    }

    /// <summary>Gets the traffic side, which decides the roundabout direction.</summary>
    public CarTrafficSide TrafficSide { get; init; }

    /// <summary>Gets the lanes, left to right (CarPlay lane guidance from iOS 17.4; Android Auto needs <see cref="LanesImage"/>).</summary>
    public CarArray<CarLane> Lanes { get; init; }

    /// <summary>Gets the image of the lanes, which Android Auto requires to show <see cref="Lanes"/>.</summary>
    public CarImage? LanesImage { get; init; }

    /// <summary>Gets the junction image.</summary>
    public CarImage? JunctionImage { get; init; }

    /// <summary>Gets the estimates to the maneuver when it becomes the next one.</summary>
    public CarTravelEstimates? InitialEstimates { get; init; }
}

/// <summary>One route of a trip (CarPlay <c>CPRouteChoice</c>, Android Auto route row).</summary>
public sealed record CarRouteChoice : CarElement
{
    /// <summary>Creates the route.</summary>
    /// <param name="summary">The summary, such as the main road.</param>
    public CarRouteChoice(CarText summary) => Summary = summary;

    /// <summary>Gets the summary.</summary>
    public CarText Summary { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets additional information, such as tolls or traffic.</summary>
    public CarText? AdditionalInformation { get; init; }

    /// <summary>Gets the summary shown when the route is selected (CarPlay).</summary>
    public CarText? SelectionSummary { get; init; }

    /// <summary>Gets the estimates to the destination on this route.</summary>
    public CarTravelEstimates? Estimates { get; init; }

    /// <summary>Gets the first maneuvers, shown when the trip starts on this route or the session reroutes to it.</summary>
    public CarArray<CarManeuver> Maneuvers { get; init; }

    /// <summary>
    /// Gets the legs of a route with stops, in order; empty means one leg from the origin to the destination. CarPlay
    /// (iOS 26.4) receives them as route segments and shares them with the car when
    /// <see cref="ICarNavigationSession.UpdateCurrentSegmentAsync(int)"/> and route sharing are used; Android Auto shows the
    /// remaining stops in the trip.
    /// </summary>
    public CarArray<CarRouteSegment> Segments { get; init; }
}

/// <summary>A place on a route: a stop, the origin or the destination (CarPlay iOS 26.4 <c>CPNavigationWaypoint</c>, Android Auto <c>Destination</c>).</summary>
public sealed record CarWaypoint
{
    /// <summary>Creates the waypoint.</summary>
    /// <param name="location">The location.</param>
    public CarWaypoint(CarLocation location) => Location = location;

    /// <summary>Gets the location.</summary>
    public CarLocation Location { get; }

    /// <summary>Gets the name.</summary>
    public CarText? Name { get; init; }

    /// <summary>Gets the address, with a new line between its parts.</summary>
    public string? Address { get; init; }
}

/// <summary>One leg of a route, between two waypoints (CarPlay iOS 26.4 <c>CPRouteSegment</c>).</summary>
public sealed record CarRouteSegment
{
    /// <summary>Creates the leg.</summary>
    /// <param name="origin">Where the leg starts.</param>
    /// <param name="destination">Where the leg ends: a stop or the trip destination.</param>
    public CarRouteSegment(CarWaypoint origin, CarWaypoint destination)
    {
        Origin = origin;
        Destination = destination;
    }

    /// <summary>Gets where the leg starts.</summary>
    public CarWaypoint Origin { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets where the leg ends.</summary>
    public CarWaypoint Destination { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the points along the leg, which route sharing sends to the car.</summary>
    public CarArray<CarLocation> Path { get; init; }

    /// <summary>Gets the maneuvers of the leg.</summary>
    public CarArray<CarManeuver> Maneuvers { get; init; }

    /// <summary>Gets the estimates from the start of the leg to its destination.</summary>
    public CarTravelEstimates? Estimates { get; init; }
}

/// <summary>Why the session reroutes (CarPlay iOS 26.4 <c>CPRerouteReason</c>).</summary>
public enum CarRerouteReason
{
    /// <summary>Not specified.</summary>
    Unknown,

    /// <summary>The driver missed a turn.</summary>
    MissedTurn,

    /// <summary>The app lost its connection and routes offline.</summary>
    Offline,

    /// <summary>The driver chose an alternate route.</summary>
    AlternateRoute,

    /// <summary>A waypoint was added, removed or changed.</summary>
    WaypointModified,

    /// <summary>The route had to change, for example because of a closure.</summary>
    Mandated,
}

/// <summary>Which system provides the route the car follows when the phone shares it (CarPlay iOS 26.4 <c>CPRouteSource</c>).</summary>
public enum CarRouteSource
{
    /// <summary>No route is active.</summary>
    Inactive,

    /// <summary>The car uses the phone's route unchanged.</summary>
    PhoneUnchanged,

    /// <summary>The car modified the phone's route, but not its destinations.</summary>
    PhoneRouteModified,

    /// <summary>The car modified the phone's route and destinations.</summary>
    PhoneRouteAndDestinationsModified,

    /// <summary>The car uses only the phone's destinations.</summary>
    PhoneDestinationsOnly,

    /// <summary>The car uses its own route and destinations.</summary>
    Vehicle,
}

/// <summary>A trip from an origin to a destination with route choices (CarPlay <c>CPTrip</c>, Android Auto route preview).</summary>
public sealed record CarTrip : CarElement
{
    /// <summary>Creates the trip.</summary>
    /// <param name="origin">The origin.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="routes">The route choices; at least one, each with its own <see cref="CarElement.Id"/>.</param>
    public CarTrip(CarLocation origin, CarLocation destination, IEnumerable<CarRouteChoice> routes)
    {
        var array = routes?.ToCarArray() ?? throw new ArgumentNullException(nameof(routes));
        if (array.IsEmpty)
        {
            throw new ArgumentException("A trip needs at least one route.", nameof(routes));
        }

        if (array.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count() != array.Count)
        {
            throw new ArgumentException("Each route of a trip needs its own Id.", nameof(routes));
        }

        Origin = origin;
        Destination = destination;
        Routes = array;
    }

    /// <summary>Gets the origin.</summary>
    public CarLocation Origin { get; }

    /// <summary>Gets the destination.</summary>
    public CarLocation Destination { get; }

    /// <summary>Gets the destination name.</summary>
    public CarText? DestinationName { get; init; }

    /// <summary>Gets the destination address (Android Auto; CarPlay iOS 26.4 waypoint address).</summary>
    public CarText? DestinationAddress { get; init; }

    /// <summary>Gets a value indicating whether CarPlay may send the destination to the car's built-in navigation (iOS 26.4 build, iOS 26.1).</summary>
    public bool IsDestinationShareable { get; init; }

    /// <summary>Gets the route choices.</summary>
    public CarArray<CarRouteChoice> Routes { get; }
}

/// <summary>An alert over the map during navigation (CarPlay <c>CPNavigationAlert</c>, Android Auto <c>Alert</c>).</summary>
public sealed record CarNavigationAlert : CarElement
{
    /// <summary>Creates the alert.</summary>
    /// <param name="title">The title.</param>
    /// <param name="primaryAction">The primary action, which CarPlay requires; selecting it dismisses the alert.</param>
    public CarNavigationAlert(CarText title, CarAction primaryAction)
    {
        Title = title;
        PrimaryAction = primaryAction;
    }

    /// <summary>Gets the title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the subtitle.</summary>
    public CarText? Subtitle { get; init; }

    /// <summary>Gets the image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the primary action.</summary>
    public CarAction PrimaryAction { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the secondary action.</summary>
    public CarAction? SecondaryAction { get; init; }

    /// <summary>Gets how long the alert stays; Google recommends 10 seconds, the default.</summary>
    public TimeSpan Duration
    {
        get;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Alert duration must be positive.");
            }

            field = value;
        }
    } = TimeSpan.FromSeconds(10);
}
