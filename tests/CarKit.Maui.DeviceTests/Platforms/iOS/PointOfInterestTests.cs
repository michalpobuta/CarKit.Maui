using CarKit.Maui.Renderers;
using CarPlay;
using CoreLocation;
using Foundation;
using MapKit;
using UIKit;

namespace CarKit.Maui.DeviceTests;

// Stage 7: CPPointOfInterestTemplate from the real renderer (without a CarPlay connection) and voice state animations
// (spec: "Category decisions (Stage 7)")
internal static class PointOfInterestTests
{
    // 16×16 PNG
    private static readonly byte[] _png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGOQW2BDEmIY1TCqYfhqAADLcvoBKIOPIQAAAABJRU5ErkJggg==");
    private static readonly CarLocation _cityCenter = new(52.2297, 21.0122);
    private static readonly CarLocation _northgate = new(52.2906, 20.9300);

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Poi_renders_points_map_items_and_buttons), Poi_renders_points_map_items_and_buttons),
        new(nameof(Poi_pin_images_load), Poi_pin_images_load),
        new(nameof(Poi_delegate_routes_selection_and_region), Poi_delegate_routes_selection_and_region),
        new(nameof(Poi_selection_after_update_uses_the_current_handler), Poi_selection_after_update_uses_the_current_handler),
        new(nameof(Poi_updates_in_place), Poi_updates_in_place),
        new(nameof(Poi_update_keeps_the_drivers_selection), Poi_update_keeps_the_drivers_selection),
        new(nameof(Poi_session_truncates_to_twelve_points), Poi_session_truncates_to_twelve_points),
        new(nameof(Poi_hooks_configure_the_native_template), Poi_hooks_configure_the_native_template),
        new(nameof(Voice_control_animates_frames), Voice_control_animates_frames),
    ];

    private sealed class Fixture
    {
        public Fixture() =>
            Context = new CarPlayRenderContext(Test.Session, Test, new CarImageLoader<UIImage>(new CarPlayImages(2), Test, combineDarkVariants: true), 2, () => CarContentStyle.Light, _ => Task.CompletedTask);

        public TestSession Test { get; } = TestSession.CarPlay();

        public CarPlayRenderContext Context { get; }
    }

    private static CarPointOfInterestTemplate Map(Func<CarItemContext, Task>? onSelected = null) => new("Parking lots")
    {
        Id = "map",
        Points =
        [
            new CarPointOfInterest(_cityCenter, "Downtown")
            {
                Id = "a",
                Subtitle = "10 Elm Street",
                Summary = "42 free",
                DetailTitle = "Downtown Parking",
                DetailSubtitle = "Open 24 h",
                DetailSummary = "$6/h",
                PrimaryAction = new CarAction("Details") { Id = "details" },
                SecondaryAction = new CarAction("Navigate") { Id = "navigate" },
                OnSelected = onSelected,
            },
            new CarPointOfInterest(_northgate, "Northgate")
            {
                Id = "b",
                SecondaryAction = new CarAction("Disabled") { IsEnabled = false },
            },
        ],
        SelectedPointId = "b",
    };

    private static CLLocationCoordinate2D Coordinate(MKMapItem item)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(26))
        {
            return item.Location.Coordinate;
        }

#pragma warning disable CA1422 // Placemark is obsolete since iOS 26; the path for older simulators
        return item.Placemark.Coordinate;
#pragma warning restore CA1422
    }

    private static string? Id(CPPointOfInterest point) => (point.UserInfo as NSString)?.ToString();

    private static Task Poi_renders_points_map_items_and_buttons()
    {
        var f = new Fixture();
        var model = Map() with { TrailingBarButtons = [new CarBarButton("Filter")] };

        var native = Check.Is<CPPointOfInterestTemplate>(CarPlayTemplateRenderers.Render(model, f.Context).Native);

        Check.Equal("Parking lots", native.Title);
        Check.Equal(2, native.PointsOfInterest.Length);
        var a = native.PointsOfInterest[0];
        Check.Equal("Downtown", a.Title);
        Check.Equal("10 Elm Street", a.Subtitle);
        Check.Equal("42 free", a.Summary);
        Check.Equal("Downtown Parking", a.DetailTitle);
        Check.Equal("Open 24 h", a.DetailSubtitle);
        Check.Equal("$6/h", a.DetailSummary);
        Check.Equal("Downtown", a.Location.Name);
        Check.Equal(52.2297, Math.Round(Coordinate(a.Location).Latitude, 4));
        Check.Equal(21.0122, Math.Round(Coordinate(a.Location).Longitude, 4));
        Check.Equal("Details", a.PrimaryButton?.Title);
        Check.Equal("Navigate", a.SecondaryButton?.Title);
        Check.Equal("a", Id(a));
        Check.Null(native.PointsOfInterest[1].SecondaryButton);
        Check.Equal(52.2906, Math.Round(Coordinate(native.PointsOfInterest[1].Location).Latitude, 4));
        Check.Equal((nint)1, native.SelectedIndex);
        Check.Equal(1, native.TrailingNavigationBarButtons.Length);
        Check.Equal(model.Id, (native.UserInfo as NSString)?.ToString());
        Check.Equal((nint)NSRange.NotFound, Check.Is<CPPointOfInterestTemplate>(CarPlayTemplateRenderers.Render(model with { SelectedPointId = null }, f.Context).Native).SelectedIndex);
        return Task.CompletedTask;
    }

    private static async Task Poi_pin_images_load()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<Stream>();
        var model = new CarPointOfInterestTemplate("Pins")
        {
            Points =
            [
                new CarPointOfInterest(_cityCenter, "From bytes") { PinImage = CarImage.FromBytes(_png), SelectedPinImage = CarImage.FromBytes(_png) },
                new CarPointOfInterest(_northgate, "From stream") { PinImage = CarImage.FromStream(_ => gate.Task) },
            ],
        };

        var native = Check.Is<CPPointOfInterestTemplate>(CarPlayTemplateRenderers.Render(model, f.Context).Native);

        Check.NotNull(native.PointsOfInterest[0].PinImage);
        Check.True(!OperatingSystem.IsIOSVersionAtLeast(16) || native.PointsOfInterest[0].SelectedPinImage is not null);
        Check.Null(native.PointsOfInterest[1].PinImage);
        gate.SetResult(new MemoryStream(_png));
        await Check.Eventually(() => native.PointsOfInterest[1].PinImage is not null, "late pin image");
        Check.Equal(2, native.PointsOfInterest.Length);
    }

    private static async Task Poi_delegate_routes_selection_and_region()
    {
        var f = new Fixture();
        string? selected = null;
        CarMapRegion? region = null;
        var model = Map(context =>
        {
            selected = context.Element.Id;
            return Task.CompletedTask;
        }) with
        {
            OnMapRegionChanged = context =>
            {
                region = context.Region;
                return Task.CompletedTask;
            },
        };
        var native = Check.Is<CPPointOfInterestTemplate>(CarPlayTemplateRenderers.Render(model, f.Context).Native);
        var pointsDelegate = Check.NotNull(native.PointOfInterestDelegate);

        pointsDelegate.DidSelectPointOfInterest(native, native.PointsOfInterest[0]);
        pointsDelegate.DidChangeMapRegion(native, new MKCoordinateRegion(new CLLocationCoordinate2D(52.25, 21.0), new MKCoordinateSpan(0.05, 0.08)));
        pointsDelegate.DidSelectPointOfInterest(native, native.PointsOfInterest[1]);

        await Check.Eventually(() => selected == "a" && region is not null, "selection and region handlers");
        Check.Equal(52.25, region!.Value.Center.Latitude);
        Check.Equal(0.05, region.Value.LatitudeDelta);
        Check.Equal(0.08, region.Value.LongitudeDelta);
        Check.Equal(0, f.Test.Errors.Count);
    }

    // Review Focus 2: the delegate gets an object from the previous SetPointsOfInterest, and the handler comes from the
    // current model
    private static async Task Poi_selection_after_update_uses_the_current_handler()
    {
        var f = new Fixture();
        var calls = new List<string>();
        var state = CarPlayTemplateRenderers.Render(Map(_ =>
        {
            calls.Add("old");
            return Task.CompletedTask;
        }), f.Context);
        var native = Check.Is<CPPointOfInterestTemplate>(state.Native);
        var rendered = native.PointsOfInterest[0];

        var updated = Map(_ =>
        {
            calls.Add("new");
            return Task.CompletedTask;
        });
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, f.Context));
        native.PointOfInterestDelegate!.DidSelectPointOfInterest(native, rendered);

        await Check.Eventually(() => calls.Count == 1, "selection handler");
        Check.Equal("new", calls[0]);
    }

    private static Task Poi_updates_in_place()
    {
        var f = new Fixture();
        var model = Map();
        var state = CarPlayTemplateRenderers.Render(model, f.Context);
        var native = Check.Is<CPPointOfInterestTemplate>(state.Native);
        var first = native.PointsOfInterest[0];

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, model with { SelectedPointId = "a" }, f.Context));
        Check.Same(native, state.Native);
        Check.Equal((nint)0, native.SelectedIndex);
        Check.Same(first, native.PointsOfInterest[0]);

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, model with { Title = "Parking lots (2)", SelectedPointId = "a" }, f.Context));
        Check.Equal("Parking lots (2)", native.Title);
        Check.Same(first, native.PointsOfInterest[0]);

        var fewer = model with { Title = "Parking lots (2)", Points = [model.Points[1] with { Summary = "7 free" }], SelectedPointId = null };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, fewer, f.Context));
        Check.Equal(1, native.PointsOfInterest.Length);
        Check.Equal("7 free", native.PointsOfInterest[0].Summary);
        Check.Equal((nint)NSRange.NotFound, native.SelectedIndex);
        return Task.CompletedTask;
    }

    // Final review I2: CarPlay reports a selection but not a deselection, so an update with SelectedPointId = null (e.g. new
    // counts of free spaces) leaves the point selected by the driver; an explicit SelectedPointId wins, a removed point →
    // no selection
    private static Task Poi_update_keeps_the_drivers_selection()
    {
        var f = new Fixture();
        var model = Map() with { SelectedPointId = null };
        var state = CarPlayTemplateRenderers.Render(model, f.Context);
        var native = Check.Is<CPPointOfInterestTemplate>(state.Native);
        Check.Equal((nint)NSRange.NotFound, native.SelectedIndex);

        native.PointOfInterestDelegate!.DidSelectPointOfInterest(native, native.PointsOfInterest[1]);
        var refreshed = model with { Points = [model.Points[0] with { Summary = "41 free" }, model.Points[1] with { Summary = "6 free" }] };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, refreshed, f.Context));
        Check.Equal("6 free", native.PointsOfInterest[1].Summary);
        Check.Equal((nint)1, native.SelectedIndex);

        var reordered = refreshed with { Points = [refreshed.Points[1], refreshed.Points[0]] };
        CarPlayTemplateRenderers.Update(state, reordered, f.Context);
        Check.Equal((nint)0, native.SelectedIndex);

        CarPlayTemplateRenderers.Update(state, reordered with { SelectedPointId = "a" }, f.Context);
        Check.Equal((nint)1, native.SelectedIndex);

        CarPlayTemplateRenderers.Update(state, reordered with { Points = [reordered.Points[0]], SelectedPointId = null }, f.Context);
        Check.Equal((nint)NSRange.NotFound, native.SelectedIndex);
        return Task.CompletedTask;
    }

    // Review Focus 3: CarPlay does not provide the POI limit at runtime; the session validator truncates to 12 from the
    // rules
    private static async Task Poi_session_truncates_to_twelve_points()
    {
        var f = new Fixture();
        var model = new CarPointOfInterestTemplate("Many")
        {
            Points = [.. Enumerable.Range(1, 15).Select(i => new CarPointOfInterest(_cityCenter, $"P{i}") { Id = $"p{i}" })],
        };

        await f.Test.Session.SetRootAsync(model);
        var sent = Check.Is<CarPointOfInterestTemplate>(f.Test.Templates[^1]);
        var native = Check.Is<CPPointOfInterestTemplate>(CarPlayTemplateRenderers.Render(sent, f.Context).Native);

        Check.Equal(12, native.PointsOfInterest.Length);
        Check.Equal("P12", native.PointsOfInterest[^1].Title);
    }

    private static Task Poi_hooks_configure_the_native_template()
    {
        var f = new Fixture();
        var runs = 0;
        var model = Map().ConfigureCarPlay(native =>
        {
            runs++;
            native.Title = "From hook";
        });

        var state = CarPlayTemplateRenderers.Render(model, f.Context);
        Check.Equal("From hook", ((CPPointOfInterestTemplate)state.Native).Title);
        CarPlayTemplateRenderers.Update(state, model with { SelectedPointId = "a" }, f.Context);

        Check.Equal(2, runs);
        Check.Equal(0, f.Test.Errors.Count);
        return Task.CompletedTask;
    }

    private static Task Voice_control_animates_frames()
    {
        var f = new Fixture();
        var voice = new CarVoiceControlTemplate([
            new CarVoiceControlState("listening", "Listening")
            {
                AnimationImages = [CarImage.FromBytes(_png), CarImage.FromBytes(_png), CarImage.FromBytes(_png)],
                AnimationDuration = TimeSpan.FromMilliseconds(600),
                Repeats = true,
            },
            new CarVoiceControlState("thinking", "Thinking")
            {
                Image = CarImage.FromBytes(_png),
                AnimationImages = [CarImage.FromBytes(_png), CarImage.FromStream(_ => Task.FromResult<Stream>(new MemoryStream(_png)))],
            },
        ]);

        var native = Check.Is<CPVoiceControlTemplate>(CarPlayTemplateRenderers.Render(voice, f.Context).Native);

        var animated = Check.NotNull(native.VoiceControlStates[0].Image);
        Check.Equal(3, animated.Images?.Length ?? 0);
        Check.Equal(0.6, Math.Round(animated.Duration, 3));
        Check.True(native.VoiceControlStates[0].Repeats);
        // A frame from a stream is not available right away → a static image
        var fallback = Check.NotNull(native.VoiceControlStates[1].Image);
        Check.Null(fallback.Images);
        return Task.CompletedTask;
    }
}
