using CarKit.Maui;

namespace CarKit.Sample.Navigation;

// Car screens (spec: "Navigation module decisions (Stage 8)", Sample): a CarMapTemplate root with map and bar buttons,
// a list of destinations → route previews in the map template (CarPlay ShowTripPreviews, Android Auto
// MapWithContentTemplate)
public sealed class SampleNavigationCarHandler(SampleRouteSimulator simulator, SampleMapState map) : ICarAppHandler
{
    public static CarMapTemplate Map(ICarSession session, SampleRouteSimulator simulator, SampleMapState map) => new()
    {
        Id = "map",
        IsPanningEnabled = true,
        StartTripTitle = "Go",
        MapButtons =
        [
            new CarMapButton(CarImage.FromSystemSymbol("location.fill", "ic_nav_center")) { Id = "center", OnSelected = _ => Run(map.Center) },
            new CarMapButton(CarImage.FromSystemSymbol("plus.magnifyingglass", "ic_nav_zoom_in")) { Id = "zoom-in", OnSelected = _ => Run(() => map.ZoomBy(1.5f)) },
            new CarMapButton(CarImage.FromSystemSymbol("minus.magnifyingglass", "ic_nav_zoom_out")) { Id = "zoom-out", OnSelected = _ => Run(() => map.ZoomBy(1 / 1.5f)) },
        ],
        TrailingBarButtons = [new CarBarButton("Destination") { Id = "destinations", OnSelected = _ => session.PushAsync(Destinations(session, simulator)) }],
    };

    public static CarListTemplate Destinations(ICarSession session, SampleRouteSimulator simulator) => new("Where are we going?")
    {
        Id = "destinations",
        Sections =
        [
            new CarSection(SampleRoutes.Destinations.Select(destination => new CarListItem(destination.Name)
            {
                Id = "destination-" + destination.Id,
                Detail = destination.Address,
                OnSelected = async _ =>
                {
                    await session.PopToRootAsync();
                    await simulator.PreviewAsync(destination);
                },
            })),
        ],
    };

    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => session.SetRootAsync(Map(session, simulator, map));

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
