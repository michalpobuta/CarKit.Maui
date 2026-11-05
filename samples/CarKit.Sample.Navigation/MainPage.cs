using CarKit.Maui;
using CarKit.Maui.Navigation;

namespace CarKit.Sample.Navigation;

// Phone screen: the same navigation session and map as in the car; simulation buttons, state, latest steps, the iOS map
// switch
public sealed class MainPage : ContentPage
{
    private readonly ICarNavigationSession _navigation;
    private readonly SampleRouteSimulator _simulator;
    private readonly Label _state = new() { FontSize = 18 };
    private readonly Label _log = new() { FontSize = 13 };
    private readonly GraphicsView _map;
    private readonly List<string> _lines = [];

    public MainPage(ICarNavigationSession navigation, SampleRouteSimulator simulator, SampleMapRenderer renderer, SampleMapState map, ICarMapSurfaces surfaces)
    {
        _navigation = navigation;
        _simulator = simulator;
        Title = "CarKit Navigation";
        _map = new GraphicsView { Drawable = renderer, HeightRequest = 260 };
        map.Changed += (_, _) => _map.Invalidate();
        navigation.StateChanged += (_, _) => Show();
        simulator.Progress += (_, line) => MainThread.BeginInvokeOnMainThread(() =>
        {
            _lines.Insert(0, line);
            _log.Text = string.Join(Environment.NewLine, _lines.Take(8));
            Show();
            _map.Invalidate();
        });

        var buttons = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Button("Route preview: Work", () => simulator.PreviewAsync(SampleRoutes.Destinations[0])),
                Button("Start (first route)", () => StartAsync(0)),
                // Stage 9: a route with a stop at a charger (two segments; CarPlay iOS 26.4: CPRouteSegment)
                Button("Start: route with a stop", () => StartAsync(1)),
                Button("Cancel", () => navigation.State == CarNavigationState.Idle ? Task.CompletedTask : CancelAsync()),
            },
        };
#if IOS
        // iOS: MapKit in the CarPlay window (native view mode) or a map drawn by the app
        buttons.Children.Add(Button("CarPlay map: MapKit / drawn", () =>
        {
            if (_mapKit = !_mapKit)
            {
                surfaces.CarScreen.UseViewController(() => new SampleMapKitController(simulator));
            }
            else
            {
                surfaces.CarScreen.UseRenderer(renderer);
            }

            return Task.CompletedTask;
        }));
#endif
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 12,
                Children = { new Label { Text = "Navigation (Stages 8–9)", FontSize = 24, FontAttributes = FontAttributes.Bold }, _state, _map, buttons, _log },
            },
        };
        Show();
    }

    // Stage 9: a run without touch (screenshots in the ledger): simctl launch with
    // SIMCTL_CHILD_CARKIT_SAMPLE_AUTORUN=stop-route, adb shell am start … --es carkit.autorun stop-route (MainActivity
    // passes it to an environment variable before the page is first shown; the app creates the page earlier than the
    // activity)
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_autorun && Environment.GetEnvironmentVariable("CARKIT_SAMPLE_AUTORUN") == "stop-route")
        {
            _autorun = true;
            Dispatcher.Dispatch(async () => await StartAsync(1));
        }
    }

    private bool _autorun;

#if IOS
    private bool _mapKit;
#endif

    private async Task StartAsync(int route)
    {
        if (_navigation.State != CarNavigationState.Previewing)
        {
            await _simulator.PreviewAsync(SampleRoutes.Destinations[0]);
        }

        var trip = _navigation.TripPreviews[0];
        _simulator.Start(trip, trip.Routes[route]);
    }

    private Task CancelAsync()
    {
        _simulator.Stop();
        return _navigation.CancelAsync();
    }

    private void Show() => MainThread.BeginInvokeOnMainThread(() =>
        _state.Text = $"State: {_navigation.State}"
            + (_navigation.Route is { Segments.Count: > 1 } route ? $" · segment {_navigation.CurrentSegmentIndex + 1}/{route.Segments.Count}" : string.Empty)
            + (_navigation.UpcomingManeuvers.Count > 0 ? $" · {_navigation.UpcomingManeuvers[0].Instruction.Text}" : string.Empty));

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) => await action();
        return button;
    }
}
