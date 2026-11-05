namespace CarKit.Maui.Testing;

/// <summary>A navigation change shown by the car, recorded by <see cref="FakeCarHost"/>.</summary>
public enum FakeCarNavigationOperation
{
    /// <summary>The car connected and showed the current navigation state.</summary>
    Restore,

    /// <summary>Trip previews were shown.</summary>
    ShowTripPreviews,

    /// <summary>Trip previews were hidden.</summary>
    HideTripPreviews,

    /// <summary>Guidance started.</summary>
    StartTrip,

    /// <summary>The upcoming maneuvers changed.</summary>
    UpdateManeuvers,

    /// <summary>The travel estimates changed.</summary>
    UpdateEstimates,

    /// <summary>An alert was shown.</summary>
    ShowAlert,

    /// <summary>The trip continued on a new route.</summary>
    Reroute,

    /// <summary>Guidance was paused.</summary>
    Pause,

    /// <summary>The trip finished.</summary>
    Finish,

    /// <summary>The previews or the trip were cancelled.</summary>
    Cancel,

    /// <summary>The car ended navigation.</summary>
    EndedByCar,

    /// <summary>Guidance moved to another leg of the route.</summary>
    UpdateSegment,
}

/// <summary>A navigation change shown by the car, with the navigation state after it.</summary>
/// <param name="Operation">The operation.</param>
/// <param name="State">The navigation state after the change.</param>
/// <param name="OnMainThread">Whether the car received the change on the (simulated) main thread.</param>
public sealed record FakeCarNavigationCall(FakeCarNavigationOperation Operation, CarNavigationState State, bool OnMainThread)
{
    /// <summary>Gets the trip previews.</summary>
    public IReadOnlyList<CarTrip> Trips { get; init; } = [];

    /// <summary>Gets the selected preview while previewing, otherwise the trip being navigated.</summary>
    public CarTrip? Trip { get; init; }

    /// <summary>Gets the route being navigated.</summary>
    public CarRouteChoice? Route { get; init; }

    /// <summary>Gets the upcoming maneuvers.</summary>
    public IReadOnlyList<CarManeuver> Maneuvers { get; init; } = [];

    /// <summary>Gets the estimates to the destination.</summary>
    public CarTravelEstimates? TripEstimates { get; init; }

    /// <summary>Gets the estimates to the next maneuver.</summary>
    public CarTravelEstimates? ManeuverEstimates { get; init; }

    /// <summary>Gets the alert shown by <see cref="FakeCarNavigationOperation.ShowAlert"/>, or the alert on screen.</summary>
    public CarNavigationAlert? Alert { get; init; }

    /// <summary>Gets the pause reason.</summary>
    public CarNavigationPauseReason? PauseReason { get; init; }

    /// <summary>Gets the pause description.</summary>
    public CarText? PauseDescription { get; init; }

    /// <summary>Gets the index of the current leg in <see cref="CarRouteChoice.Segments"/>.</summary>
    public int SegmentIndex { get; init; }

    /// <summary>Gets the reason of the last reroute.</summary>
    public CarRerouteReason? RerouteReason { get; init; }
}

// Stage 8: the host's navigation session (spec: "Navigation module decisions (Stage 8)", FakeCarHost). The state
// outlives the connection, as in the app: the car gets it after ConnectAsync (Restore), and driver simulations go
// through the host input path.
public sealed partial class FakeCarHost
{
    private readonly CarNavigationController _navigation;
    private readonly List<FakeCarNavigationCall> _navigationCalls = [];
    private IDisposable? _navigationAttachment;

    /// <summary>
    /// Gets the navigation session of the host, the same object an app gets from dependency injection with
    /// <c>car.UseNavigation(...)</c>. Its state outlives connections; a connected car shows it in <see cref="NavigationCalls"/>.
    /// </summary>
    public ICarNavigationSession Navigation => _navigation;

    /// <summary>Gets or sets the handler that receives what the simulated driver does.</summary>
    public ICarNavigationHandler? NavigationHandler { get; set; }

    /// <summary>Gets the navigation changes the connected car showed.</summary>
    public IReadOnlyList<FakeCarNavigationCall> NavigationCalls
    {
        get
        {
            lock (_navigationCalls)
            {
                return [.. _navigationCalls];
            }
        }
    }

    /// <summary>Simulates the driver selecting a route in the trip previews.</summary>
    /// <param name="trip">A previewed trip.</param>
    /// <param name="route">One of its routes.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnTripPreviewSelectedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or does not show the trip as a preview.</exception>
    public Task SelectTripPreviewAsync(CarTrip trip, CarRouteChoice route)
    {
        Previewed(trip, route);
        return _navigation.OnPreviewSelectedAsync(trip.Id, route.Id);
    }

    /// <summary>Simulates the driver pressing Go (CarPlay) or Start (Android Auto) in the trip previews.</summary>
    /// <param name="trip">A previewed trip.</param>
    /// <param name="route">One of its routes.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnTripStartRequestedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or does not show the trip as a preview.</exception>
    public Task StartTripFromCarAsync(CarTrip trip, CarRouteChoice route)
    {
        Previewed(trip, route);
        return _navigation.OnStartRequestedAsync(trip.Id, route.Id);
    }

    /// <summary>
    /// Simulates the car ending navigation (CarPlay: the driver ends the route; Android Auto: <c>onStopNavigation</c>). The
    /// session goes idle, then <see cref="ICarNavigationHandler.OnNavigationEndedByCarAsync"/> runs.
    /// </summary>
    /// <returns>A task that completes when the handler completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected.</exception>
    public Task EndNavigationFromCarAsync()
    {
        Connected();
        return _navigation.OnEndedByCarAsync();
    }

    /// <summary>Simulates the driver tapping a maneuver notification while the app is in the background (CarPlay).</summary>
    /// <param name="maneuver">An upcoming maneuver.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnManeuverSelectedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is not CarPlay.</exception>
    public Task SelectManeuverAsync(CarManeuver maneuver)
    {
        ArgumentNullException.ThrowIfNull(maneuver);
        CarPlayOnly("Maneuver notifications");
        return _navigation.OnManeuverSelectedAsync(maneuver.Id);
    }

    /// <summary>Simulates the driver tapping a navigation alert notification while the app is in the background (CarPlay).</summary>
    /// <param name="alert">The shown alert.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnNavigationAlertSelectedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is not CarPlay.</exception>
    public Task SelectNavigationAlertAsync(CarNavigationAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        CarPlayOnly("Navigation alert notifications");
        return _navigation.OnAlertSelectedAsync(alert.Id);
    }

    /// <summary>Simulates a voice command or another app asking to navigate (Android Auto <c>ACTION_NAVIGATE</c>).</summary>
    /// <param name="uri">The request, such as <c>geo:0,0?q=address</c>.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnNavigationRequestedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is CarPlay.</exception>
    public Task RequestNavigationAsync(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        AndroidOnly("Navigation intents");
        return _navigation.OnNavigationRequestedAsync(new CarNavigationRequest(new Uri(uri)));
    }

    /// <summary>Simulates the car asking the app to simulate driving (Android Auto <c>onAutoDriveEnabled</c>).</summary>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnAutoDriveRequestedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is CarPlay.</exception>
    public Task EnableAutoDriveAsync()
    {
        AndroidOnly("Auto drive");
        return _navigation.OnAutoDriveRequestedAsync();
    }

    /// <summary>
    /// Simulates the car's built-in navigation proposing a stop for a leg of the shared route (CarPlay iOS 26.4 route
    /// sharing).
    /// </summary>
    /// <param name="waypoint">The proposed stop.</param>
    /// <param name="segmentIndex">The leg of the route.</param>
    /// <returns>The estimates the app returned from <see cref="ICarNavigationHandler.OnWaypointInsertRequestedAsync"/>.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected, is not CarPlay with route sharing, or no route is being navigated.</exception>
    public Task<CarTravelEstimates?> RequestWaypointInsertAsync(CarWaypoint waypoint, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        RouteSharing("Waypoint proposals", needsTrip: true);
        return _navigation.OnWaypointInsertRequestedAsync(waypoint, segmentIndex);
    }

    /// <summary>Simulates the driver accepting or declining a stop the car proposed (CarPlay iOS 26.4 route sharing).</summary>
    /// <param name="waypoint">The proposed stop.</param>
    /// <param name="accepted">Whether the driver accepted it.</param>
    /// <param name="segmentIndex">The leg of the route, when the car says.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnWaypointProposalAnsweredAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected, is not CarPlay with route sharing, or no route is being navigated.</exception>
    public Task AnswerWaypointProposalAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        RouteSharing("Waypoint proposals", needsTrip: true);
        return _navigation.OnWaypointProposalAnsweredAsync(waypoint, accepted, segmentIndex);
    }

    /// <summary>Simulates the car asking to navigate to a destination (CarPlay iOS 26.4).</summary>
    /// <param name="destination">The destination.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnDestinationRequestedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is not CarPlay with route sharing.</exception>
    public Task RequestDestinationAsync(CarWaypoint destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        RouteSharing("Destination requests", needsTrip: false);
        return _navigation.OnDestinationRequestedAsync(destination);
    }

    /// <summary>Simulates the car reporting which system provides the route it follows (CarPlay iOS 26.4 route sharing).</summary>
    /// <param name="source">The route source.</param>
    /// <returns>A task that completes when <see cref="ICarNavigationHandler.OnRouteSourceChangedAsync"/> completes.</returns>
    /// <exception cref="InvalidOperationException">The car is not connected or is not CarPlay with route sharing.</exception>
    public Task ChangeRouteSourceAsync(CarRouteSource source)
    {
        RouteSharing("Route source updates", needsTrip: false);
        return _navigation.OnRouteSourceChangedAsync(source);
    }

    private async Task AttachNavigationAsync()
    {
        DetachNavigation();
        _navigationAttachment = await _navigation.AttachAsync(new NavigationRecorder(this)).ConfigureAwait(false);
    }

    private void DetachNavigation()
    {
        _navigationAttachment?.Dispose();
        _navigationAttachment = null;
    }

    private void Previewed(CarTrip trip, CarRouteChoice route)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(route);
        Connected();
        var state = _navigation.Snapshot;
        if (state.State != CarNavigationState.Previewing || !state.Previews.Any(t => t.Id == trip.Id))
        {
            throw new InvalidOperationException($"The car does not show trip '{trip.Id}' as a preview; the navigation session is {state.State}.");
        }
    }

    private void CarPlayOnly(string feature)
    {
        Connected();
        if (Platform != CarPlatform.CarPlay)
        {
            throw new InvalidOperationException($"{feature} are CarPlay only; Android Auto shows no notification to select.");
        }
    }

    // Stage 9: car requests come only from CarPlay with route sharing (iOS 26.4, asset 26.4)
    private void RouteSharing(string feature, bool needsTrip)
    {
        var session = Connected();
        if (Platform != CarPlatform.CarPlay || !session.Capabilities.Supports(CarFeature.RouteSharing))
        {
            throw new InvalidOperationException($"{feature} come from CarPlay with route sharing (iOS 26.4) only.");
        }

        if (needsTrip && _navigation.State is not (CarNavigationState.Active or CarNavigationState.Paused))
        {
            throw new InvalidOperationException($"{feature} need a route being navigated; the navigation session is {_navigation.State}.");
        }
    }

    private void AndroidOnly(string feature)
    {
        Connected();
        if (Platform == CarPlatform.CarPlay)
        {
            throw new InvalidOperationException($"{feature} come from Android Auto only.");
        }
    }

    // The action of a shown navigation alert: closing (session state) before the handler
    private CarAction? NavigationAlertAction(CarAction action)
    {
        if (_navigation.Snapshot.Alert is not { } alert)
        {
            return null;
        }

        CarAction?[] actions = [alert.PrimaryAction, alert.SecondaryAction];
        var match = Array.Find(actions, a => ReferenceEquals(a, action)) ?? Array.Find(actions, a => a?.Id == action.Id);
        if (match is not null)
        {
            _navigation.OnAlertDismissed(alert.Id);
        }

        return match;
    }

    private void Record(FakeCarNavigationOperation operation, CarNavigationSnapshot state, CarNavigationAlert? alert = null)
    {
        var call = new FakeCarNavigationCall(operation, state.State, !Dispatcher.IsDispatchRequired)
        {
            Trips = state.Previews,
            Trip = state.State == CarNavigationState.Previewing ? state.SelectedPreview : state.Trip,
            Route = state.Route,
            Maneuvers = state.Maneuvers,
            TripEstimates = state.TripEstimates,
            ManeuverEstimates = state.ManeuverEstimates,
            Alert = alert ?? state.Alert,
            PauseReason = state.PauseReason,
            PauseDescription = state.PauseDescription,
            SegmentIndex = state.SegmentIndex,
            RerouteReason = state.RerouteReason,
        };
        lock (_navigationCalls)
        {
            _navigationCalls.Add(call);
        }
    }

    private sealed class NavigationRecorder(FakeCarHost host) : ICarNavigationSink
    {
        public void Apply(CarNavigationChange change, CarNavigationSnapshot state)
        {
            switch (change)
            {
                case CarNavigationChange.PreviewSelected:
                    // The car already shows the driver's choice
                    break;
                case CarNavigationChange.AlertShown shown:
                    host.Record(FakeCarNavigationOperation.ShowAlert, state, shown.Alert);
                    break;
                default:
                    host.Record(Operation(change), state);
                    break;
            }
        }

        public void Restore(CarNavigationSnapshot state) => host.Record(FakeCarNavigationOperation.Restore, state);

        private static FakeCarNavigationOperation Operation(CarNavigationChange change) => change switch
        {
            CarNavigationChange.PreviewsShown => FakeCarNavigationOperation.ShowTripPreviews,
            CarNavigationChange.PreviewsHidden => FakeCarNavigationOperation.HideTripPreviews,
            CarNavigationChange.TripStarted => FakeCarNavigationOperation.StartTrip,
            CarNavigationChange.ManeuversUpdated => FakeCarNavigationOperation.UpdateManeuvers,
            CarNavigationChange.EstimatesUpdated => FakeCarNavigationOperation.UpdateEstimates,
            CarNavigationChange.Rerouted => FakeCarNavigationOperation.Reroute,
            CarNavigationChange.Paused => FakeCarNavigationOperation.Pause,
            CarNavigationChange.Finished => FakeCarNavigationOperation.Finish,
            CarNavigationChange.Cancelled => FakeCarNavigationOperation.Cancel,
            CarNavigationChange.EndedByCar => FakeCarNavigationOperation.EndedByCar,
            CarNavigationChange.SegmentChanged => FakeCarNavigationOperation.UpdateSegment,
            _ => throw new ArgumentOutOfRangeException(nameof(change), change.GetType().Name, "Unknown navigation change."),
        };
    }
}
