using CarKit.Maui.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;

namespace CarKit.Maui.DeviceTests;

// Stage 8: navigation handler of the test app (driver actions via the host path)
internal sealed class DeviceTestNavigationHandler : ICarNavigationHandler
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public void Clear()
    {
        lock (_calls)
        {
            _calls.Clear();
        }
    }

    public Task OnTripPreviewSelectedAsync(CarTrip trip, CarRouteChoice route) => Record($"preview:{trip.Id}/{route.Id}");

    public Task OnTripStartRequestedAsync(CarTrip trip, CarRouteChoice route) => Record($"start:{trip.Id}/{route.Id}");

    public Task OnNavigationEndedByCarAsync() => Record("ended");

    public Task OnManeuverSelectedAsync(CarManeuver maneuver) => Record("maneuver:" + maneuver.Id);

    public Task OnNavigationAlertSelectedAsync(CarNavigationAlert alert) => Record("alert:" + alert.Id);

    public Task OnNavigationRequestedAsync(CarNavigationRequest request) => Record("request:" + (request.Query ?? request.Location?.ToString()));

    public Task OnAutoDriveRequestedAsync() => Record("autodrive");

    // Stage 9: car requests when sharing a route (CarPlay iOS 26.4)
    public async Task<CarTravelEstimates?> OnWaypointInsertRequestedAsync(CarWaypoint waypoint, int segmentIndex)
    {
        await Record($"insert:{waypoint.Name?.Text}@{segmentIndex}");
        return new CarTravelEstimates(new CarDistance(2, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(4));
    }

    public Task OnWaypointProposalAnsweredAsync(CarWaypoint waypoint, bool accepted, int? segmentIndex) =>
        Record($"answer:{waypoint.Name?.Text}:{accepted}@{segmentIndex}");

    public Task OnDestinationRequestedAsync(CarWaypoint destination) => Record("destination:" + destination.Name?.Text);

    public Task OnRouteSourceChangedAsync(CarRouteSource source) => Record("source:" + source);

    private Task Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }

        return Task.CompletedTask;
    }
}

// Module services from the test app's MauiApp; the navigation session is a singleton, so every test starts from Idle
internal static class NavigationTestServices
{
    public static IServiceProvider Services => IPlatformApplication.Current!.Services;

    public static CarNavigationRuntime Runtime => Services.GetRequiredService<CarNavigationRuntime>();

    public static CarNavigationController Navigation => Runtime.Navigation;

    public static DeviceTestNavigationHandler Handler => Services.GetRequiredService<DeviceTestNavigationHandler>();

    public static async Task ResetAsync()
    {
        if (Navigation.State != CarNavigationState.Idle)
        {
            await Navigation.CancelAsync();
        }

        Handler.Clear();
    }

    public static CarTrip Trip() => new(new CarLocation(52.2297, 21.0122), new CarLocation(52.1672, 20.9679),
    [
        new CarRouteChoice("Via Riverside Drive")
        {
            Id = "fast",
            AdditionalInformation = "Fastest",
            SelectionSummary = "25 min",
            Estimates = new CarTravelEstimates(new CarDistance(12.4, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(25)) { TimeRemainingColor = CarTravelEstimatesColor.Green },
            Maneuvers =
            [
                new CarManeuver(new CarText(["Turn right onto Main Street", "Right"]))
                {
                    Id = "m1",
                    Kind = CarManeuverKind.Right,
                    Road = "Main Street",
                    Icon = CarImage.FromSystemSymbol("arrow.turn.up.right", "ic_carkit_test"),
                    InitialEstimates = new CarTravelEstimates(new CarDistance(300, CarDistanceUnit.Meters), TimeSpan.FromSeconds(40)),
                    Lanes =
                    [
                        new CarLane([new CarLaneDirection(CarLaneShape.Straight, false)]),
                        new CarLane([new CarLaneDirection(CarLaneShape.Straight, false), new CarLaneDirection(CarLaneShape.Right, true)]),
                    ],
                    LanesImage = CarImage.FromSystemSymbol("road.lanes", "ic_carkit_test"),
                },
                new CarManeuver("At the roundabout, third exit")
                {
                    Id = "m2",
                    Kind = CarManeuverKind.Roundabout,
                    RoundaboutExitNumber = 3,
                    InitialEstimates = new CarTravelEstimates(new CarDistance(1.2, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(2)),
                },
            ],
        },
        new CarRouteChoice("Via downtown") { Id = "eco", Estimates = new CarTravelEstimates(new CarDistance(10.1, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(31)) },
    ])
    {
        Id = "work",
        DestinationName = "Work",
        DestinationAddress = "39 Oak Street",
    };

    public static CarMapTemplate Map(Func<CarItemContext, Task>? onButton = null) => new()
    {
        Id = "map",
        MapButtons =
        [
            new CarMapButton(CarImage.FromSystemSymbol("location.fill", "ic_carkit_test")) { Id = "center", OnSelected = onButton },
            new CarMapButton(CarImage.FromSystemSymbol("plus.magnifyingglass", "ic_carkit_test")) { Id = "zoom", IsEnabled = false },
            new CarMapButton(CarImage.FromSystemSymbol("minus.magnifyingglass", "ic_carkit_test")) { Id = "hidden", IsHidden = true },
        ],
        TrailingBarButtons = [new CarBarButton("Destination") { Id = "destination" }],
        LeadingBarButtons = [new CarBarButton("Menu") { Id = "menu" }],
        GuidanceBackgroundColor = new CarColor(20, 40, 80),
        AutomaticallyHidesNavigationBar = false,
        IsPanningEnabled = true,
        StartTripTitle = "Go",
    };
}

// Test renderer: fills the surface with one color and records the areas
internal sealed class SolidRenderer(Color color) : ICarSurfaceRenderer
{
    public int Frames { get; private set; }

    public RectF Visible { get; private set; }

    public RectF Stable { get; private set; }

    public float Offset { get; set; }

    public void Draw(ICanvas canvas, RectF visibleArea, RectF stableArea)
    {
        Frames++;
        Visible = visibleArea;
        Stable = stableArea;
        canvas.FillColor = color;
        canvas.FillRectangle(-10, -10, 10000, 10000);
        // Moving element (frame measurement)
        canvas.FillColor = Colors.White;
        canvas.FillCircle(50 + (Offset % 200), 50, 10);
    }
}
