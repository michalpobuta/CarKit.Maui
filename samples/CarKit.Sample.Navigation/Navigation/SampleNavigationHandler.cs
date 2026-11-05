using CarKit.Maui;

namespace CarKit.Sample.Navigation;

// Driver and car actions (ICarNavigationHandler) → driving simulation
public sealed class SampleNavigationHandler(SampleRouteSimulator simulator) : ICarNavigationHandler
{
    public Task OnTripStartRequestedAsync(CarTrip trip, CarRouteChoice route)
    {
        simulator.Start(trip, route);
        return Task.CompletedTask;
    }

    // The car ended navigation (CarPlay: the end of the driver's route, Android Auto: onStopNavigation); the session is
    // already Idle
    public Task OnNavigationEndedByCarAsync()
    {
        simulator.Stop();
        return Task.CompletedTask;
    }

    // Google Assistant or another app: "navigate to …" → a route preview to a known destination
    public Task OnNavigationRequestedAsync(CarNavigationRequest request) =>
        SampleRoutes.Find(request.Query) is { } destination ? simulator.PreviewAsync(destination) : Task.CompletedTask;

    // DHU --autodrive / adb dumpsys … AUTO_DRIVE: start the first previewed route
    public Task OnAutoDriveRequestedAsync()
    {
        simulator.AutoDrive = true;
        return Task.CompletedTask;
    }

    // Stage 9 (CarPlay iOS 26.4, nav.ShareRoutesWithCar): the car proposes a stop (e.g. a charger); the estimate to it
    // comes from the straight-line distance; after acceptance a real app recalculates the route (RerouteAsync with
    // CarRerouteReason.WaypointModified)
    public Task<CarTravelEstimates?> OnWaypointInsertRequestedAsync(CarWaypoint waypoint, int segmentIndex)
    {
        simulator.ReportCarRequest($"stop {waypoint.Name?.Text ?? "unnamed"} on segment {segmentIndex + 1}");
        return Task.FromResult<CarTravelEstimates?>(SampleRoutes.Estimates(1.5, 4));
    }

    public Task OnWaypointProposalAnsweredAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex)
    {
        simulator.ReportCarRequest($"stop {waypoint.Name?.Text} {(accepted ? "accepted" : "rejected")}");
        return Task.CompletedTask;
    }

    // The car asks for navigation to a destination: a route preview to the known destination with that name
    public Task OnDestinationRequestedAsync(CarWaypoint destination)
    {
        simulator.ReportCarRequest("destination " + destination.Name?.Text);
        return SampleRoutes.Find(destination.Name?.Text) is { } known ? simulator.PreviewAsync(known) : Task.CompletedTask;
    }

    public Task OnRouteSourceChangedAsync(CarRouteSource source)
    {
        simulator.ReportCarRequest("route source " + source);
        return Task.CompletedTask;
    }
}
