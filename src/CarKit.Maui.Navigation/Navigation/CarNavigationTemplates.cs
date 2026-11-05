using CarKit.Maui.Renderers;

namespace CarKit.Maui.Navigation;

// What NavigationTemplate (Android Auto) shows in a given navigation state
internal enum CarNavigationInfoKind
{
    // Without route info (a map for browsing)
    None,

    // RoutingInfo with the current and next step
    Routing,

    // RoutingInfo while loading (route calculation, positioning)
    Loading,

    // MessageInfo (arrival, getting to the route)
    Message,
}

// Decisions of the module renderers without platform types (spec: "Navigation module decisions (Stage 8)"), tested on
// net10.0
internal static class CarNavigationTemplates
{
    // MapWithContentTemplate has @RequiresCarApi(7); below that, the deprecated RoutePreviewNavigationTemplate
    public const int MapWithContentApiLevel = 7;

    // Cluster session (SessionInfo.DISPLAY_TYPE_CLUSTER) from Car App API 6
    public const int ClusterApiLevel = 6;

    // AppManager.showAlert from Car App API 5 (like CarAlertTemplate in Stage 2)
    public const int AlertApiLevel = 5;

    // Android Auto template type for CarMapTemplate in a navigation state (task steps counted before rendering)
    public static string MapTemplateType(CarNavigationState state, int apiLevel) =>
        state == CarNavigationState.Previewing
            ? apiLevel >= MapWithContentApiLevel ? CarAppTemplateTypes.MapWithContent : CarAppTemplateTypes.RoutePreview
            : CarAppTemplateTypes.Navigation;

    public static CarNavigationInfoKind InfoKind(CarNavigationSnapshot state) => state.State switch
    {
        CarNavigationState.Active => state.Maneuvers.IsEmpty ? CarNavigationInfoKind.Loading : CarNavigationInfoKind.Routing,
        CarNavigationState.Paused => state.PauseReason is CarNavigationPauseReason.Arrived or CarNavigationPauseReason.ProceedToRoute
            ? CarNavigationInfoKind.Message
            : CarNavigationInfoKind.Loading,
        _ => CarNavigationInfoKind.None,
    };

    // MessageInfo title: pause description, destination name, route summary
    public static CarText MessageTitle(CarNavigationSnapshot state) =>
        state.PauseDescription ?? state.Trip?.DestinationName ?? state.Route?.Summary ?? (CarText)state.PauseReason.GetValueOrDefault().ToString();

    // Distance to the next maneuver: an estimate from UpdateEstimatesAsync or the maneuver's initial estimate
    public static CarDistance? DistanceToNext(CarNavigationSnapshot state) =>
        state.ManeuverEstimates?.DistanceRemaining ?? (state.Maneuvers.IsEmpty ? null : state.Maneuvers[0].InitialEstimates?.DistanceRemaining);

    // Categories in which the app draws the map on a surface (ACCESS_SURFACE permission from the targets)
    public static bool HasMapSurface(CarAppCategory categories) =>
        (categories & (CarAppCategory.Navigation | CarAppCategory.Parking | CarAppCategory.EvCharging | CarAppCategory.Fueling | CarAppCategory.Weather)) != 0;

    // POI with the app map (MapWithContentTemplate): API 7, a category with a surface and CarScreen content at
    // connection time
    public static bool UsesAppMapForPointsOfInterest(int apiLevel, CarAppCategory categories, bool carScreenHasContent) =>
        apiLevel >= MapWithContentApiLevel && HasMapSurface(categories) && carScreenHasContent;

    // CarPlay CPLane (iOS 17.4): shape angles in degrees, 0 = straight, negative to the left
    public static double LaneAngle(CarLaneShape shape) => shape switch
    {
        CarLaneShape.SlightLeft => -45,
        CarLaneShape.Left => -90,
        CarLaneShape.SharpLeft => -135,
        CarLaneShape.UTurnLeft => -180,
        CarLaneShape.SlightRight => 45,
        CarLaneShape.Right => 90,
        CarLaneShape.SharpRight => 135,
        CarLaneShape.UTurnRight => 180,
        _ => 0,
    };

    // Arrival: Android Auto TravelEstimate requires an arrival time
    public static DateTimeOffset ArrivalTime(CarTravelEstimates estimates, DateTimeOffset now) => estimates.ArrivalTime ?? now + estimates.TimeRemaining;
}
