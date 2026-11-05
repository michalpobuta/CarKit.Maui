using System.Globalization;

namespace CarKit.Maui;

/// <summary>
/// Turn-by-turn navigation shown on every connected car screen: the CarPlay map template, its dashboard and
/// instrument cluster, and the Android Auto navigation template and cluster. There is one session per app (from
/// <c>car.UseNavigation(...)</c> in dependency injection); guidance keeps running on the phone when no car is connected,
/// and a car that connects shows the current state right away.
/// </summary>
/// <remarks>
/// Calls are serialized and run the native calls on the main thread. A call outside its state (for example
/// <see cref="UpdateManeuversAsync"/> without an active trip) throws <see cref="InvalidOperationException"/>; a native
/// error of a car screen throws <see cref="CarPlatformException"/> after the state has changed. What the driver does in
/// the car reaches <see cref="ICarNavigationHandler"/>.
/// </remarks>
public interface ICarNavigationSession
{
    /// <summary>Gets the state.</summary>
    CarNavigationState State { get; }

    /// <summary>Gets the trip being navigated (active or paused).</summary>
    CarTrip? Trip { get; }

    /// <summary>Gets the route being navigated.</summary>
    CarRouteChoice? Route { get; }

    /// <summary>Gets the trips shown as previews.</summary>
    IReadOnlyList<CarTrip> TripPreviews { get; }

    /// <summary>Gets the upcoming maneuvers, the next one first.</summary>
    IReadOnlyList<CarManeuver> UpcomingManeuvers { get; }

    /// <summary>Gets the index of the current leg in <see cref="CarRouteChoice.Segments"/> of <see cref="Route"/>; 0 when the trip starts.</summary>
    int CurrentSegmentIndex => 0;

    /// <summary>Raised on the main thread when <see cref="State"/> changes.</summary>
    event EventHandler<CarNavigationState> StateChanged;

    /// <summary>Shows trip previews for the driver to pick a route; an empty list hides them.</summary>
    /// <param name="trips">The trips, each with its own <see cref="CarElement.Id"/>.</param>
    /// <param name="selected">The selected trip, one of <paramref name="trips"/>.</param>
    /// <returns>A task that completes when the car screens show the previews.</returns>
    Task ShowTripPreviewsAsync(IReadOnlyList<CarTrip> trips, CarTrip? selected);

    /// <summary>Starts guidance on a route of the trip, with the route's <see cref="CarRouteChoice.Maneuvers"/>.</summary>
    /// <param name="trip">The trip.</param>
    /// <param name="route">One of <see cref="CarTrip.Routes"/>.</param>
    /// <returns>A task that completes when the car screens show the guidance.</returns>
    Task StartTripAsync(CarTrip trip, CarRouteChoice route);

    /// <summary>Replaces the upcoming maneuvers; with maneuvers, a paused trip resumes.</summary>
    /// <param name="upcoming">The maneuvers, the next one first (icon, instruction, lanes, distance).</param>
    /// <returns>A task that completes when the car screens show the maneuvers.</returns>
    Task UpdateManeuversAsync(IReadOnlyList<CarManeuver> upcoming);

    /// <summary>Updates the estimates to the destination and to the next maneuver.</summary>
    /// <param name="trip">The estimates to the destination.</param>
    /// <param name="maneuver">The estimates to the next maneuver.</param>
    /// <returns>A task that completes when the car screens show the estimates.</returns>
    Task UpdateEstimatesAsync(CarTravelEstimates trip, CarTravelEstimates? maneuver);

    /// <summary>Shows an alert over the map, in any state.</summary>
    /// <param name="alert">The alert.</param>
    /// <returns>A task that completes when the car screens show the alert.</returns>
    Task ShowAlertAsync(CarNavigationAlert alert);

    /// <summary>Continues the trip on a new route with its maneuvers and estimates (CarPlay <c>CPRouteInformation</c> from iOS 17.4).</summary>
    /// <param name="route">The new route.</param>
    /// <returns>A task that completes when the car screens show the route.</returns>
    Task RerouteAsync(CarRouteChoice route);

    /// <summary>
    /// Continues the trip on a new route and tells the car why (CarPlay iOS 26.4 <c>resumeTripWithUpdatedRouteSegments</c>);
    /// the new route starts at its first leg.
    /// </summary>
    /// <param name="route">The new route.</param>
    /// <param name="reason">Why the route changed.</param>
    /// <returns>A task that completes when the car screens show the route.</returns>
    Task RerouteAsync(CarRouteChoice route, CarRerouteReason reason) => RerouteAsync(route);

    /// <summary>
    /// Moves guidance to another leg of the route, for example after the driver reaches a stop (CarPlay iOS 26.4
    /// <c>CPNavigationSession.currentSegment</c>; Android Auto shows the remaining stops).
    /// </summary>
    /// <param name="index">The index in <see cref="CarRouteChoice.Segments"/>; a route without segments has one leg.</param>
    /// <returns>A task that completes when the car screens show the leg.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a leg of the route.</exception>
    Task UpdateCurrentSegmentAsync(int index) => throw new NotSupportedException("This navigation session does not track route segments.");

    /// <summary>Pauses guidance.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>A task that completes when the car screens show the pause.</returns>
    Task PauseAsync(CarNavigationPauseReason reason);

    /// <summary>Pauses guidance with a description for the guidance card.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="description">The description; Android Auto shows it for <see cref="CarNavigationPauseReason.Arrived"/> and <see cref="CarNavigationPauseReason.ProceedToRoute"/>.</param>
    /// <returns>A task that completes when the car screens show the pause.</returns>
    Task PauseAsync(CarNavigationPauseReason reason, CarText? description);

    /// <summary>Ends the trip after the driver arrived.</summary>
    /// <returns>A task that completes when the car screens end guidance.</returns>
    Task FinishAsync();

    /// <summary>Cancels the previews or the trip.</summary>
    /// <returns>A task that completes when the car screens end guidance.</returns>
    Task CancelAsync();
}

/// <summary>
/// Implemented by the app to react to the driver and the car during navigation; register it with
/// <c>nav.UseHandler&lt;THandler&gt;()</c>. Every method has an empty default. CarKit calls them on the main thread and
/// passes exceptions to <see cref="ICarErrorHandler"/>.
/// </summary>
public interface ICarNavigationHandler
{
    /// <summary>The driver selected a route in the previews (CarPlay <c>SelectedPreview</c>, Android Auto route list).</summary>
    /// <param name="trip">The trip.</param>
    /// <param name="route">The route.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnTripPreviewSelectedAsync(CarTrip trip, CarRouteChoice route) => Task.CompletedTask;

    /// <summary>The driver pressed Go (CarPlay) or Start (Android Auto); call <see cref="ICarNavigationSession.StartTripAsync"/>.</summary>
    /// <param name="trip">The trip.</param>
    /// <param name="route">The route.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnTripStartRequestedAsync(CarTrip trip, CarRouteChoice route) => Task.CompletedTask;

    /// <summary>
    /// The driver or the car ended navigation (CarPlay <c>DidCancelNavigation</c>, Android Auto <c>onStopNavigation</c>). The
    /// session is already idle and voice prompts are stopped; stop guidance in the app as well.
    /// </summary>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnNavigationEndedByCarAsync() => Task.CompletedTask;

    /// <summary>The driver tapped a maneuver notification while the app was in the background (CarPlay).</summary>
    /// <param name="maneuver">The maneuver.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnManeuverSelectedAsync(CarManeuver maneuver) => Task.CompletedTask;

    /// <summary>The driver tapped a navigation alert notification while the app was in the background (CarPlay).</summary>
    /// <param name="alert">The alert.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnNavigationAlertSelectedAsync(CarNavigationAlert alert) => Task.CompletedTask;

    /// <summary>
    /// A voice command or another app asked to navigate (Android Auto <c>CarContext.ACTION_NAVIGATE</c>). To receive such
    /// requests while Android Auto is not running, the phone activity declares its own <c>geo:</c> intent filter.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnNavigationRequestedAsync(CarNavigationRequest request) => Task.CompletedTask;

    /// <summary>The car asked the app to simulate driving to the destination once navigation starts (Android Auto <c>onAutoDriveEnabled</c>).</summary>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnAutoDriveRequestedAsync() => Task.CompletedTask;

    /// <summary>
    /// The car's built-in navigation proposes a stop for a leg of the shared route, such as a charger (CarPlay iOS 26.4 route
    /// sharing). Return the estimates to the stop; the driver accepts or declines it next
    /// (<see cref="OnWaypointProposalAnsweredAsync"/>).
    /// </summary>
    /// <param name="waypoint">The proposed stop.</param>
    /// <param name="segmentIndex">The leg the stop belongs to.</param>
    /// <returns>The estimates to the stop, or <see langword="null"/> when unknown.</returns>
    Task<CarTravelEstimates?> OnWaypointInsertRequestedAsync(CarWaypoint waypoint, int segmentIndex) => Task.FromResult<CarTravelEstimates?>(null);

    /// <summary>The driver accepted or declined a proposed stop; reroute with <see cref="CarRerouteReason.WaypointModified"/> to add it.</summary>
    /// <param name="waypoint">The proposed stop.</param>
    /// <param name="accepted">Whether the driver accepted it.</param>
    /// <param name="segmentIndex">The leg the stop belongs to, when the car says.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnWaypointProposalAnsweredAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex) => Task.CompletedTask;

    /// <summary>The car asks to navigate to a destination; show a trip preview for it (CarPlay iOS 26.4).</summary>
    /// <param name="destination">The destination.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnDestinationRequestedAsync(CarWaypoint destination) => Task.CompletedTask;

    /// <summary>The car reports which system now provides the route it follows (CarPlay iOS 26.4 route sharing).</summary>
    /// <param name="source">The route source.</param>
    /// <returns>A task that completes when the app has reacted.</returns>
    Task OnRouteSourceChangedAsync(CarRouteSource source) => Task.CompletedTask;
}

/// <summary>A request to navigate from a voice command or another app (<c>geo:</c> or <c>google.navigation:</c>).</summary>
public sealed record CarNavigationRequest
{
    /// <summary>Creates the request and reads the location or the query from <paramref name="uri"/>.</summary>
    /// <param name="uri">The request URI.</param>
    public CarNavigationRequest(Uri uri)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        (Location, Query) = CarNavigationRequests.Parse(uri);
    }

    /// <summary>Gets the request URI.</summary>
    public Uri Uri { get; }

    /// <summary>Gets the requested location, when the URI has coordinates.</summary>
    public CarLocation? Location { get; }

    /// <summary>Gets the searched address or place name, or the label of the coordinates.</summary>
    public string? Query { get; }
}

// geo:lat,lon[;u=..][?z=..], geo:0,0?q=lat,lon(label), geo:0,0?q=address, google.navigation:q=lat,lon|address (Android)
internal static class CarNavigationRequests
{
    public static (CarLocation? Location, string? Query) Parse(Uri uri)
    {
        var text = uri.OriginalString;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return (null, null);
        }

        var scheme = text[..colon].ToLowerInvariant();
        var rest = text[(colon + 1)..];
        var question = rest.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? rest : rest[..question];
        var query = question < 0 ? string.Empty : rest[(question + 1)..];
        var q = Parameter(scheme == "google.navigation" ? rest : query, "q");
        switch (scheme)
        {
            case "geo":
                if (q is not null)
                {
                    return FromQuery(q);
                }

                return (Coordinates(path.Split(';')[0]) is { } location && !IsZero(location) ? location : null, null);
            case "google.navigation":
                return q is null ? (null, null) : FromQuery(q);
            default:
                return (null, null);
        }
    }

    // q=lat,lon(label) or address
    private static (CarLocation?, string?) FromQuery(string q)
    {
        var label = (string?)null;
        var open = q.IndexOf('(', StringComparison.Ordinal);
        var coordinates = q;
        if (open >= 0 && q.EndsWith(')'))
        {
            label = q[(open + 1)..^1];
            coordinates = q[..open];
        }

        return Coordinates(coordinates) is { } location ? (location, label) : (null, q);
    }

    private static CarLocation? Coordinates(string value)
    {
        var parts = value.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return null;
        }

        return new CarLocation(latitude, longitude);
    }

    private static bool IsZero(CarLocation location) => location.Latitude == 0 && location.Longitude == 0;

    private static string? Parameter(string query, string name)
    {
        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0 && pair[..equals] == name)
            {
                return Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }
}
