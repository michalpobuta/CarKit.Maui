using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Navigation state seen by the surfaces (an immutable snapshot)
internal sealed record CarNavigationSnapshot
{
    public static CarNavigationSnapshot Idle { get; } = new();

    public CarNavigationState State { get; init; }

    public CarArray<CarTrip> Previews { get; init; }

    public CarTrip? SelectedPreview { get; init; }

    public CarRouteChoice? SelectedRoute { get; init; }

    public CarTrip? Trip { get; init; }

    public CarRouteChoice? Route { get; init; }

    public CarArray<CarManeuver> Maneuvers { get; init; }

    public CarTravelEstimates? TripEstimates { get; init; }

    public CarTravelEstimates? ManeuverEstimates { get; init; }

    public CarNavigationPauseReason? PauseReason { get; init; }

    public CarText? PauseDescription { get; init; }

    // The last alert shown, to dismiss it (action, time, end of route)
    public CarNavigationAlert? Alert { get; init; }

    // Stage 9: the current route segment (CarNavigationSegments.Legs) and the reason for the last reroute
    public int SegmentIndex { get; init; }

    public CarRerouteReason? RerouteReason { get; init; }
}

// A state change for the surfaces: each surface performs the cheapest native operation (CarPlay mutates the
// CPNavigationSession, Android Auto rebuilds the NavigationTemplate and calls NavigationManager)
internal abstract record CarNavigationChange
{
    private CarNavigationChange()
    {
    }

    public sealed record PreviewsShown(CarArray<CarTrip> Trips, CarTrip? Selected) : CarNavigationChange;

    // The driver's choice (CarPlay already shows it; the Android Auto route list must keep the selection after a
    // rebuild)
    public sealed record PreviewSelected(CarTrip Trip, CarRouteChoice Route) : CarNavigationChange;

    public sealed record PreviewsHidden : CarNavigationChange;

    public sealed record TripStarted(CarTrip Trip, CarRouteChoice Route) : CarNavigationChange;

    // Resumed: resuming after a pause (CarPlay iOS 17.4: ResumeTrip)
    public sealed record ManeuversUpdated(CarArray<CarManeuver> Maneuvers, bool Resumed) : CarNavigationChange;

    public sealed record EstimatesUpdated(CarTravelEstimates Trip, CarTravelEstimates? Maneuver) : CarNavigationChange;

    public sealed record AlertShown(CarNavigationAlert Alert) : CarNavigationChange;

    // Stage 9: reroute reason (CarPlay iOS 26.4: resumeTripWithUpdatedRouteSegments:…rerouteReason:)
    public sealed record Rerouted(CarRouteChoice Route, CarRerouteReason Reason) : CarNavigationChange;

    // Stage 9: the driver moved to another route segment (CarPlay iOS 26.4: currentSegment; Android Auto: remaining
    // destinations)
    public sealed record SegmentChanged(int Index) : CarNavigationChange;

    public sealed record Paused(CarNavigationPauseReason Reason, CarText? Description) : CarNavigationChange;

    public sealed record Finished : CarNavigationChange;

    public sealed record Cancelled : CarNavigationChange;

    // The car ended navigation (CarPlay DidCancelNavigation, Android Auto onStopNavigation); the surfaces clean up
    public sealed record EndedByCar : CarNavigationChange;
}

// A surface that shows navigation: CarPlay (the connection's CPMapTemplate), Android Auto (screen, NavigationManager),
// the cluster, FakeCarHost. Called on the main thread; an exception = a native error (the session reports it as
// CarPlatformException).
internal interface ICarNavigationSink
{
    void Apply(CarNavigationChange change, CarNavigationSnapshot state);

    // Attaching (car connection, new map template): restore the current state
    void Restore(CarNavigationSnapshot state);
}

// One navigation session per app (spec: "Navigation module decisions (Stage 8)", Navigation session). Operations are
// serialized by the gate and run on the main thread; transitions follow the state table; events and app handlers run
// after the gate is released (a handler may call the session right away, e.g. StartTripAsync from
// OnTripStartRequestedAsync).
internal sealed class CarNavigationController : ICarNavigationSession, IDisposable
{
    private readonly ICarDispatcher _dispatcher;
    private readonly ICarErrorHandler _errors;
    private readonly Func<ICarNavigationHandler?> _handler;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly List<ICarNavigationSink> _sinks = [];
    private CarNavigationSnapshot _snapshot = CarNavigationSnapshot.Idle;

    public CarNavigationController(ICarDispatcher dispatcher, ICarErrorHandler errors, Func<ICarNavigationHandler?> handler, ILogger logger)
    {
        _dispatcher = dispatcher;
        _errors = errors;
        _handler = handler;
        _logger = logger;
    }

    public event EventHandler<CarNavigationState>? StateChanged;

    // The car ended navigation: the module stops voice prompts (Google: the app must stop speaking)
    public event EventHandler? EndedByCar;

    public CarNavigationSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                return _snapshot;
            }
        }

        private set
        {
            lock (_lock)
            {
                _snapshot = value;
            }
        }
    }

    public CarNavigationState State => Snapshot.State;

    public CarTrip? Trip => Snapshot.Trip;

    public CarRouteChoice? Route => Snapshot.Route;

    public IReadOnlyList<CarTrip> TripPreviews => Snapshot.Previews;

    public IReadOnlyList<CarManeuver> UpcomingManeuvers => Snapshot.Maneuvers;

    public int CurrentSegmentIndex => Snapshot.SegmentIndex;

    // Attaching a surface: after the current operation, with the state restored; Dispose detaches (any thread)
    public async Task<IDisposable> AttachAsync(ICarNavigationSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _dispatcher.DispatchAsync(() =>
            {
                lock (_lock)
                {
                    _sinks.Add(sink);
                }

                try
                {
                    sink.Restore(Snapshot);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _errors.HandleError(ex, new CarErrorContext("Navigation.Restore", Snapshot.Trip));
                }

                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return new Attachment(this, sink);
    }

    public Task ShowTripPreviewsAsync(IReadOnlyList<CarTrip> trips, CarTrip? selected)
    {
        ArgumentNullException.ThrowIfNull(trips);
        var previews = trips.ToCarArray();
        if (previews.Any(trip => trip is null))
        {
            throw new ArgumentException("Trip previews cannot contain null.", nameof(trips));
        }

        if (previews.Select(trip => trip.Id).Distinct(StringComparer.Ordinal).Count() != previews.Count)
        {
            throw new ArgumentException("Each trip preview needs its own Id.", nameof(trips));
        }

        if (selected is not null && !previews.Any(trip => trip.Id == selected.Id))
        {
            throw new ArgumentException($"The selected trip '{selected.Id}' is not one of the previews.", nameof(selected));
        }

        return RunAsync(nameof(ShowTripPreviewsAsync), state =>
        {
            Require(state, nameof(ShowTripPreviewsAsync), CarNavigationState.Idle, CarNavigationState.Previewing);
            if (previews.IsEmpty)
            {
                return (CarNavigationSnapshot.Idle with { Alert = state.Alert }, state.State == CarNavigationState.Previewing ? new CarNavigationChange.PreviewsHidden() : null);
            }

            var chosen = selected is null ? null : previews.First(trip => trip.Id == selected.Id);
            return (
                CarNavigationSnapshot.Idle with { State = CarNavigationState.Previewing, Previews = previews, SelectedPreview = chosen, Alert = state.Alert },
                new CarNavigationChange.PreviewsShown(previews, chosen));
        });
    }

    public Task StartTripAsync(CarTrip trip, CarRouteChoice route)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentNullException.ThrowIfNull(route);
        var chosen = trip.Routes.FirstOrDefault(r => r.Id == route.Id)
            ?? throw new ArgumentException($"Route '{route.Id}' is not one of the routes of trip '{trip.Id}'.", nameof(route));
        CheckManeuvers(chosen.Maneuvers, nameof(route));
        return RunAsync(nameof(StartTripAsync), state =>
        {
            Require(state, nameof(StartTripAsync), CarNavigationState.Idle, CarNavigationState.Previewing);
            return (
                CarNavigationSnapshot.Idle with
                {
                    State = CarNavigationState.Active,
                    Trip = trip,
                    Route = chosen,
                    Maneuvers = chosen.Maneuvers,
                    TripEstimates = chosen.Estimates,
                    Alert = state.Alert,
                },
                new CarNavigationChange.TripStarted(trip, chosen));
        });
    }

    public Task UpdateManeuversAsync(IReadOnlyList<CarManeuver> upcoming)
    {
        ArgumentNullException.ThrowIfNull(upcoming);
        var maneuvers = upcoming.ToCarArray();
        CheckManeuvers(maneuvers, nameof(upcoming));
        return RunAsync(nameof(UpdateManeuversAsync), state =>
        {
            Require(state, nameof(UpdateManeuversAsync), CarNavigationState.Active, CarNavigationState.Paused);
            var resumed = state.State == CarNavigationState.Paused && !maneuvers.IsEmpty;
            return (
                state with
                {
                    State = resumed ? CarNavigationState.Active : state.State,
                    Maneuvers = maneuvers,
                    PauseReason = resumed ? null : state.PauseReason,
                    PauseDescription = resumed ? null : state.PauseDescription,
                    // Stage 9 review (I2): resuming without a reroute — CarPlay iOS 26.4 gets the reason Unknown, not
                    // the previous one
                    RerouteReason = resumed ? null : state.RerouteReason,
                },
                new CarNavigationChange.ManeuversUpdated(maneuvers, resumed));
        });
    }

    public Task UpdateEstimatesAsync(CarTravelEstimates trip, CarTravelEstimates? maneuver)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return RunAsync(nameof(UpdateEstimatesAsync), state =>
        {
            Require(state, nameof(UpdateEstimatesAsync), CarNavigationState.Active, CarNavigationState.Paused);
            return (state with { TripEstimates = trip, ManeuverEstimates = maneuver }, new CarNavigationChange.EstimatesUpdated(trip, maneuver));
        });
    }

    public Task ShowAlertAsync(CarNavigationAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return RunAsync(nameof(ShowAlertAsync), state => (state with { Alert = alert }, new CarNavigationChange.AlertShown(alert)));
    }

    public Task RerouteAsync(CarRouteChoice route) => RerouteAsync(route, CarRerouteReason.Unknown);

    public Task RerouteAsync(CarRouteChoice route, CarRerouteReason reason)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown reroute reason.");
        }

        CheckManeuvers(route.Maneuvers, nameof(route));
        return RunAsync(nameof(RerouteAsync), state =>
        {
            Require(state, nameof(RerouteAsync), CarNavigationState.Active, CarNavigationState.Paused);
            return (
                state with
                {
                    State = CarNavigationState.Active,
                    Route = route,
                    Maneuvers = route.Maneuvers,
                    TripEstimates = route.Estimates ?? state.TripEstimates,
                    ManeuverEstimates = null,
                    PauseReason = null,
                    PauseDescription = null,
                    SegmentIndex = 0,
                    RerouteReason = reason,
                },
                new CarNavigationChange.Rerouted(route, reason));
        });
    }

    public Task UpdateCurrentSegmentAsync(int index) => RunAsync(nameof(UpdateCurrentSegmentAsync), state =>
    {
        Require(state, nameof(UpdateCurrentSegmentAsync), CarNavigationState.Active, CarNavigationState.Paused);
        var count = CarNavigationSegments.Legs(state.Trip!, state.Route!).Count;
        if (index < 0 || index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"The route has {count} segment(s).");
        }

        return index == state.SegmentIndex ? (state, null) : (state with { SegmentIndex = index }, new CarNavigationChange.SegmentChanged(index));
    });

    public Task PauseAsync(CarNavigationPauseReason reason) => PauseAsync(reason, null);

    public Task PauseAsync(CarNavigationPauseReason reason, CarText? description)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown pause reason.");
        }

        return RunAsync(nameof(PauseAsync), state =>
        {
            Require(state, nameof(PauseAsync), CarNavigationState.Active, CarNavigationState.Paused);
            return (
                state with { State = CarNavigationState.Paused, PauseReason = reason, PauseDescription = description },
                new CarNavigationChange.Paused(reason, description));
        });
    }

    public Task FinishAsync() => RunAsync(nameof(FinishAsync), state =>
    {
        Require(state, nameof(FinishAsync), CarNavigationState.Active, CarNavigationState.Paused);
        return (CarNavigationSnapshot.Idle, new CarNavigationChange.Finished());
    });

    public Task CancelAsync() => RunAsync(nameof(CancelAsync), state =>
    {
        Require(state, nameof(CancelAsync), CarNavigationState.Previewing, CarNavigationState.Active, CarNavigationState.Paused);
        return (CarNavigationSnapshot.Idle, new CarNavigationChange.Cancelled());
    });

    // Inputs from the host (native callbacks): they do not throw, the app handler runs after the gate is released

    public async Task OnPreviewSelectedAsync(string tripId, string routeId)
    {
        CarTrip? trip = null;
        CarRouteChoice? route = null;
        await RunSafelyAsync("PreviewSelected", state =>
        {
            trip = state.State == CarNavigationState.Previewing ? state.Previews.FirstOrDefault(t => t.Id == tripId) : null;
            route = trip?.Routes.FirstOrDefault(r => r.Id == routeId);
            return trip is null || route is null
                ? (state, null)
                : (state with { SelectedPreview = trip, SelectedRoute = route }, new CarNavigationChange.PreviewSelected(trip, route));
        }).ConfigureAwait(false);
        await InvokeAsync("TripPreviewSelected", trip, trip is null || route is null ? null : h => h.OnTripPreviewSelectedAsync(trip, route), tripId).ConfigureAwait(false);
    }

    public Task OnStartRequestedAsync(string tripId, string routeId)
    {
        var state = Snapshot;
        var trip = state.Previews.FirstOrDefault(t => t.Id == tripId) ?? (state.Trip?.Id == tripId ? state.Trip : null);
        var route = trip?.Routes.FirstOrDefault(r => r.Id == routeId);
        return InvokeAsync("TripStartRequested", trip, trip is null || route is null ? null : h => h.OnTripStartRequestedAsync(trip, route), tripId);
    }

    public async Task OnEndedByCarAsync()
    {
        var ended = false;
        await RunSafelyAsync("EndedByCar", state =>
        {
            ended = state.State != CarNavigationState.Idle;
            return ended ? (CarNavigationSnapshot.Idle, new CarNavigationChange.EndedByCar()) : (state, null);
        }).ConfigureAwait(false);
        if (!ended)
        {
            return;
        }

        await _dispatcher.DispatchAsync(() =>
        {
            RaiseSafely(() => EndedByCar?.Invoke(this, EventArgs.Empty), "Navigation.EndedByCar");
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        await InvokeAsync("NavigationEndedByCar", null, h => h.OnNavigationEndedByCarAsync(), null).ConfigureAwait(false);
    }

    public Task OnManeuverSelectedAsync(string maneuverId)
    {
        var state = Snapshot;
        var maneuver = state.Maneuvers.FirstOrDefault(m => m.Id == maneuverId) ?? state.Route?.Maneuvers.FirstOrDefault(m => m.Id == maneuverId);
        return InvokeAsync("ManeuverSelected", maneuver, maneuver is null ? null : h => h.OnManeuverSelectedAsync(maneuver), maneuverId);
    }

    public Task OnAlertSelectedAsync(string alertId)
    {
        var alert = Snapshot.Alert is { } shown && shown.Id == alertId ? shown : null;
        return InvokeAsync("NavigationAlertSelected", alert, alert is null ? null : h => h.OnNavigationAlertSelectedAsync(alert), alertId);
    }

    public Task OnNavigationRequestedAsync(CarNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return InvokeAsync("NavigationRequested", null, h => h.OnNavigationRequestedAsync(request), null);
    }

    public Task OnAutoDriveRequestedAsync() => InvokeAsync("AutoDriveRequested", null, h => h.OnAutoDriveRequestedAsync(), null);

    // Stage 9: car requests when sharing a route (CarPlay iOS 26.4). The result of a stop request goes to the CarPlay
    // completion, so a missing handler or an exception (to ICarErrorHandler) yields null, not an exception
    public async Task<CarTravelEstimates?> OnWaypointInsertRequestedAsync(CarWaypoint waypoint, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        CarTravelEstimates? estimates = null;
        await _dispatcher.DispatchAsync(async () =>
        {
            if (_handler() is not { } handler)
            {
                return;
            }

            try
            {
                estimates = await handler.OnWaypointInsertRequestedAsync(waypoint, segmentIndex).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _errors.HandleError(ex, new CarErrorContext("Navigation.WaypointInsertRequested", null));
            }
        }).ConfigureAwait(false);
        return estimates;
    }

    public Task OnWaypointProposalAnsweredAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        return InvokeAsync("WaypointProposalAnswered", null, h => h.OnWaypointProposalAnsweredAsync(waypoint, accepted, segmentIndex), null);
    }

    public Task OnDestinationRequestedAsync(CarWaypoint destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return InvokeAsync("DestinationRequested", null, h => h.OnDestinationRequestedAsync(destination), null);
    }

    public Task OnRouteSourceChangedAsync(CarRouteSource source) =>
        InvokeAsync("RouteSourceChanged", null, h => h.OnRouteSourceChangedAsync(source), null);

    // The alert disappeared from the car (action, time, driver); state only, no surfaces
    public void OnAlertDismissed(string alertId)
    {
        lock (_lock)
        {
            if (_snapshot.Alert?.Id == alertId)
            {
                _snapshot = _snapshot with { Alert = null };
            }
        }
    }

    private static void Require(CarNavigationSnapshot state, string operation, params CarNavigationState[] allowed)
    {
        if (!allowed.Contains(state.State))
        {
            throw new InvalidOperationException(
                $"{operation} needs the navigation session in {string.Join(" or ", allowed)}; it is {state.State}.");
        }
    }

    // A roundabout without an exit number is rejected by both platforms (RoundaboutExitN, Maneuver.Builder); Ids map
    // native objects
    private static void CheckManeuvers(CarArray<CarManeuver> maneuvers, string parameter)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var maneuver in maneuvers)
        {
            ArgumentNullException.ThrowIfNull(maneuver, parameter);
            if (!ids.Add(maneuver.Id))
            {
                throw new ArgumentException($"Maneuver Id '{maneuver.Id}' is used twice; give each maneuver its own Id.", parameter);
            }

            if (maneuver.Kind == CarManeuverKind.Roundabout && maneuver.RoundaboutExitNumber is null)
            {
                throw new ArgumentException($"Maneuver '{maneuver.Id}' is a roundabout and needs RoundaboutExitNumber.", parameter);
            }
        }
    }

    private async Task RunAsync(string operation, Func<CarNavigationSnapshot, (CarNavigationSnapshot Next, CarNavigationChange? Change)> transition)
    {
        Exception? failure;
        (CarNavigationState Before, CarNavigationState After) states;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            (failure, states) = await _dispatcher.DispatchAsync(() => Task.FromResult(Transition(transition))).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RaiseStateChangedAsync(states).ConfigureAwait(false);
        if (failure is not null)
        {
            throw new CarPlatformException($"Navigation {operation} failed on a car screen: {failure.Message}", failure);
        }
    }

    // Host inputs: a native surface error goes to ICarErrorHandler instead of an exception in the platform callback
    private async Task RunSafelyAsync(string operation, Func<CarNavigationSnapshot, (CarNavigationSnapshot Next, CarNavigationChange? Change)> transition)
    {
        Exception? failure;
        (CarNavigationState Before, CarNavigationState After) states;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            (failure, states) = await _dispatcher.DispatchAsync(() => Task.FromResult(Transition(transition))).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await RaiseStateChangedAsync(states).ConfigureAwait(false);
        if (failure is not null)
        {
            _errors.HandleError(failure, new CarErrorContext("Navigation." + operation, null));
        }
    }

    // On the main thread: the new state, then the change for each surface (one surface's error does not stop the
    // others)
    private (Exception? Failure, (CarNavigationState, CarNavigationState) States) Transition(
        Func<CarNavigationSnapshot, (CarNavigationSnapshot Next, CarNavigationChange? Change)> transition)
    {
        var previous = Snapshot;
        var (next, change) = transition(previous);
        Snapshot = next;
        if (change is null)
        {
            return (null, (previous.State, next.State));
        }

        ICarNavigationSink[] sinks;
        lock (_lock)
        {
            sinks = [.. _sinks];
        }

        Exception? failure = null;
        foreach (var sink in sinks)
        {
            try
            {
                sink.Apply(change, next);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure ??= ex;
            }
        }

        return (failure, (previous.State, next.State));
    }

    private Task RaiseStateChangedAsync((CarNavigationState Before, CarNavigationState After) states) =>
        states.Before == states.After
            ? Task.CompletedTask
            : _dispatcher.DispatchAsync(() =>
            {
                RaiseSafely(() => StateChanged?.Invoke(this, states.After), "Navigation.StateChanged");
                return Task.CompletedTask;
            });

    private Task InvokeAsync(string operation, CarElement? element, Func<ICarNavigationHandler, Task>? call, string? id)
    {
        if (call is null)
        {
            CarKitLog.NavigationTargetUnknown(_logger, operation, id);
            return Task.CompletedTask;
        }

        return _dispatcher.DispatchAsync(async () =>
        {
            if (_handler() is not { } handler)
            {
                return;
            }

            try
            {
                await call(handler).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _errors.HandleError(ex, new CarErrorContext("Navigation." + operation, element));
            }
        });
    }

    [SuppressMessage("Design", "CA1031", Justification = "An exception from an app event handler must not abort the navigation session operation.")]
    private void RaiseSafely(Action raise, string operation)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            _errors.HandleError(ex, new CarErrorContext(operation, null));
        }
    }

    public void Dispose() => _gate.Dispose();

    private void Detach(ICarNavigationSink sink)
    {
        lock (_lock)
        {
            _sinks.Remove(sink);
        }
    }

    private sealed class Attachment(CarNavigationController owner, ICarNavigationSink sink) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Detach(sink);
            }
        }
    }
}

// Stage 9: route segments (spec: "New platform features decisions (Stage 9)", Navigation). A route without Segments =
// one segment, start → destination, so CarPlay iOS 26.4 always gets a CPRouteSegment, and Android Auto gets the route
// destination as in Stage 8
internal static class CarNavigationSegments
{
    public static CarArray<CarRouteSegment> Legs(CarTrip trip, CarRouteChoice route) =>
        !route.Segments.IsEmpty
            ? route.Segments
            : [new CarRouteSegment(new CarWaypoint(trip.Origin), Destination(trip)) { Maneuvers = route.Maneuvers, Estimates = route.Estimates }];

    public static CarWaypoint Destination(CarTrip trip) =>
        new(trip.Destination) { Name = trip.DestinationName, Address = trip.DestinationAddress?.Text };

    // Android Auto Trip destinations: the end of each remaining segment with its estimate; the route destination — the
    // session's current estimate (or the segment's). A segment without an estimate is skipped, because
    // Trip.Builder.addDestination requires a TravelEstimate
    public static IReadOnlyList<(CarWaypoint Waypoint, CarTravelEstimates Estimates)> RemainingStops(
        CarTrip trip, CarRouteChoice route, int segmentIndex, CarTravelEstimates? tripEstimates)
    {
        var legs = Legs(trip, route);
        var stops = new List<(CarWaypoint, CarTravelEstimates)>();
        for (var i = Math.Clamp(segmentIndex, 0, legs.Count - 1); i < legs.Count; i++)
        {
            var estimates = i == legs.Count - 1 ? tripEstimates ?? legs[i].Estimates : legs[i].Estimates;
            if (estimates is not null)
            {
                stops.Add((legs[i].Destination, estimates));
            }
        }

        return stops;
    }
}
