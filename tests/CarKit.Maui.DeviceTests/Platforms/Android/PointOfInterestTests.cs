using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Serialization;
using CarKit.Maui.Renderers;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarText = AndroidX.Car.App.Model.CarText;

namespace CarKit.Maui.DeviceTests;

// Stage 7: CarPointOfInterestTemplate from the real renderer into a Car App Library model: PlaceListMapTemplate (POI
// categories) and a place list without a map; click and refresh via the host path (OnClickDelegate,
// OnContentRefreshDelegate)
internal static class PointOfInterestTests
{
    // 16×16 PNG
    private static readonly byte[] _png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGOQW2BDEmIY1TCqYfhqAADLcvoBKIOPIQAAAABJRU5ErkJggg==");
    private static readonly Handler _main = new(Looper.MainLooper!);
    private static readonly CarLocation _cityCenter = new(52.2297, 21.0122);

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Poi_renders_place_list_map_with_places_and_distances), Poi_renders_place_list_map_with_places_and_distances),
        new(nameof(Poi_rows_have_distance_and_marker_without_image), Poi_rows_have_distance_and_marker_without_image),
        new(nameof(Poi_click_and_refresh_reach_the_handlers), Poi_click_and_refresh_reach_the_handlers),
        new(nameof(Poi_without_poi_category_renders_list_rows_with_places), Poi_without_poi_category_renders_list_rows_with_places),
        new(nameof(Poi_loading_and_hooks), Poi_loading_and_hooks),
        new(nameof(Distance_units_match_the_binding), Distance_units_match_the_binding),
        new(nameof(Poi_without_refresh_button_logs_a_warning_once), Poi_without_refresh_button_logs_a_warning_once),
    ];

    private sealed class Fixture
    {
        public Fixture(int apiLevel = 8, bool placeListMap = true)
        {
            Test = TestSession.AndroidAuto(apiLevel);
            var app = Android.App.Application.Context;
            Context = new CarAppRenderContext(
                Test.Session, Test, apiLevel, app, new CarImageLoader<CarIcon>(new CarAppImages(app), Test, combineDarkVariants: false),
                () => CarContentStyle.Light, work => _main.Post(work), useSectionedItemTemplate: false, Test, usePlaceListMap: placeListMap);
            Screen = new CarAppScreenState { IsRoot = true, Rerender = () => Rerenders++ };
        }

        public TestSession Test { get; }

        public CarAppRenderContext Context { get; }

        public CarAppScreenState Screen { get; }

        public int Rerenders { get; set; }

        public CarAppRendered Render(CarTemplate template) => CarAppTemplateRenderers.Render(template, Context, Screen);
    }

    private sealed class Done : Java.Lang.Object, IOnDoneCallback
    {
        public void OnFailure(Bundleable? response)
        {
        }

        public void OnSuccess(Bundleable? response)
        {
        }
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
                Distance = new CarDistance(1.25, CarDistanceUnit.Kilometers),
                PinImage = CarImage.FromBytes(_png),
                OnSelected = onSelected,
            },
            new CarPointOfInterest(new CarLocation(52.2906, 20.93), "Northgate") { Id = "b", Distance = new CarDistance(350, CarDistanceUnit.Meters) },
        ],
    };

    private static string? Text(NativeCarText? text) => text?.ToCharSequence();

    private static List<Row> Rows(ItemList list) => [.. list.Items!.Select(item => ((IJavaObject)item).JavaCast<Row>()!)];

    // CarText.toCharSequence() reproduces the text with spans (CarText.Spans is an internal API, XAOBS001)
    private static AndroidX.Car.App.Model.Distance? DistanceOf(NativeCarText text)
    {
        var spanned = text.ToCharSequenceFormatted().JavaCast<Android.Text.ISpanned>();
        var spans = spanned?.GetSpans(0, spanned.Length(), Java.Lang.Class.FromType(typeof(DistanceSpan)));
        return spans is { Length: > 0 } ? spans[0].JavaCast<DistanceSpan>()?.Distance : null;
    }

    private static Task Poi_renders_place_list_map_with_places_and_distances()
    {
        var f = new Fixture();
        var rendered = f.Render(Map() with { TrailingBarButtons = [new CarBarButton("Filter")] });

        var template = Check.Is<PlaceListMapTemplate>(rendered.Template);
        Check.Equal("Parking lots", Text(template.Title));
        Check.Equal(NativeAction.TypeAppIcon, template.HeaderAction!.Type);
        Check.Equal(1, template.ActionStrip!.Actions!.Count);
        var rows = Rows(template.ItemList!);
        Check.Equal(2, rows.Count);
        Check.Equal("Downtown", Text(rows[0].Title));
        var texts = rows[0].Texts!;
        Check.True(Text(texts[0])!.EndsWith("· 10 Elm Street", StringComparison.Ordinal));
        Check.Equal("42 free", Text(texts[1]));
        var distance = Check.NotNull(DistanceOf(texts[0]));
        Check.Equal(1.25, distance.DisplayDistance);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitKilometersP1, distance.DisplayUnit);
        var place = Check.NotNull(rows[0].Metadata?.Place);
        Check.Equal(52.2297, place.Location!.Latitude);
        Check.Equal(21.0122, place.Location.Longitude);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitMeters, Check.NotNull(DistanceOf(rows[1].Texts![0])).DisplayUnit);
        Check.Null(rows[0].OnClickDelegate);
        Check.Equal(CarAppTemplateTypes.PlaceListMap, rendered.Shape.Type);
        Check.Equal("Downtown,Northgate", string.Join(',', rendered.Shape.Sections[0].Rows.Select(row => row.Title)));
        return Task.CompletedTask;
    }

    // Review Focus 1: the host rejects a row without a distance and a row with both a marker and an image; the renderer
    // gives the pin only to the marker
    private static Task Poi_rows_have_distance_and_marker_without_image()
    {
        var f = new Fixture();
        var rows = Rows(Check.Is<PlaceListMapTemplate>(f.Render(Map()).Template).ItemList!);

        var marker = Check.NotNull(rows[0].Metadata!.Place!.Marker);
        Check.NotNull(marker.Icon);
        Check.Equal(PlaceMarker.TypeImage, marker.IconType);
        Check.Null(rows[0].Image);
        Check.Null(Check.NotNull(rows[1].Metadata!.Place!.Marker).Icon);

        // Without a distance (validator in Warn/Off) the Car App Library rejects the list, which is why the validator
        // requires it
        var withoutDistance = Map() with { Points = [new CarPointOfInterest(_cityCenter, "No distance")] };
        var rejected = false;
        try
        {
            f.Render(withoutDistance);
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            rejected = true;
        }

        Check.True(rejected);
        return Task.CompletedTask;
    }

    private static async Task Poi_click_and_refresh_reach_the_handlers()
    {
        var f = new Fixture();
        var calls = new List<string>();
        var model = Map(context =>
        {
            calls.Add("select:" + context.Element.Id);
            return Task.CompletedTask;
        }) with
        {
            OnRefreshRequested = context =>
            {
                calls.Add("refresh:" + context.Element.Id);
                return Task.CompletedTask;
            },
        };
        model = model with
        {
            Points = [model.Points[0], model.Points[1] with { PrimaryAction = new CarAction("Details", _ => { calls.Add("primary"); return Task.CompletedTask; }) }],
        };
        var template = Check.Is<PlaceListMapTemplate>(f.Render(model).Template);
        var rows = Rows(template.ItemList!);

        rows[0].OnClickDelegate!.SendClick(new Done());
        rows[1].OnClickDelegate!.SendClick(new Done());
        Check.NotNull(template.OnContentRefreshDelegate).SendContentRefreshRequested(new Done());

        await Check.Eventually(() => calls.Count == 3, "click and refresh handlers");
        Check.Equal("select:a,primary,refresh:map", string.Join(',', calls));
        // The refresh button exists from Car App API 5
        Check.Null(Check.Is<PlaceListMapTemplate>(new Fixture(apiLevel: 4).Render(model).Template).OnContentRefreshDelegate);
    }

    private static Task Poi_without_poi_category_renders_list_rows_with_places()
    {
        var f = new Fixture(placeListMap: false);

        var rendered = f.Render(Map());

        var template = Check.Is<ListTemplate>(rendered.Template);
        Check.Equal("Parking lots", Text(template.Header!.Title));
        var rows = Rows(template.SingleList!);
        Check.Equal(2, rows.Count);
        Check.NotNull(rows[0].Image);
        Check.Null(rows[0].Metadata!.Place!.Marker);
        Check.Equal(52.2297, rows[0].Metadata!.Place!.Location!.Latitude);
        Check.NotNull(DistanceOf(rows[0].Texts![0]));
        Check.Equal(CarAppTemplateTypes.List, rendered.Shape.Type);
        return Task.CompletedTask;
    }

    private static Task Poi_loading_and_hooks()
    {
        var f = new Fixture();
        var model = Map().ConfigureAndroidAuto(builder => builder.SetCurrentLocationEnabled(true));

        var loading = f.Render(model with { IsLoading = true });
        var loadingTemplate = Check.Is<PlaceListMapTemplate>(loading.Template);
        Check.True(loadingTemplate.IsLoading);
        Check.Null(loadingTemplate.ItemList);
        Check.True(loading.Shape.IsLoading);
        Check.True(Check.Is<PlaceListMapTemplate>(f.Render(model).Template).IsCurrentLocationEnabled);

        // A list without a map has a different builder: the PlaceListMapTemplate.Builder hook does not match → a warning
        // once
        var list = new Fixture(placeListMap: false);
        list.Render(model);
        list.Render(model);
        Check.Equal(1, list.Test.Logs.Count(log => log.Contains("PlaceListMapTemplate.Builder", StringComparison.Ordinal) || log.Contains("ListTemplate.Builder", StringComparison.Ordinal)));
        Check.Equal(0, f.Test.Errors.Count);
        return Task.CompletedTask;
    }

    // Final review I1: a list without a map (a non-POI category) and a host below Car App API 5 have no refresh button; a
    // warning once per template instead of silently skipping the handler
    private static Task Poi_without_refresh_button_logs_a_warning_once()
    {
        var model = Map() with { OnRefreshRequested = _ => Task.CompletedTask };
        static int Warnings(Fixture fixture) => fixture.Test.Logs.Count(log => log.Contains("no refresh button", StringComparison.Ordinal));

        var list = new Fixture(placeListMap: false);
        list.Render(model);
        list.Render(model);
        var oldHost = new Fixture(apiLevel: 4);
        Check.Null(Check.Is<PlaceListMapTemplate>(oldHost.Render(model).Template).OnContentRefreshDelegate);
        var current = new Fixture();
        Check.NotNull(Check.Is<PlaceListMapTemplate>(current.Render(model).Template).OnContentRefreshDelegate);

        Check.Equal(1, Warnings(list));
        Check.Equal(1, Warnings(oldHost));
        Check.Equal(0, Warnings(current));
        return Task.CompletedTask;
    }

    // CarDistanceUnits (net10.0) uses numeric constants from the javadoc: pinned to the binding
    private static Task Distance_units_match_the_binding()
    {
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitMeters, CarDistanceUnits.AndroidAuto(new CarDistance(1, CarDistanceUnit.Meters)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitKilometers, CarDistanceUnits.AndroidAuto(new CarDistance(12, CarDistanceUnit.Kilometers)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitKilometersP1, CarDistanceUnits.AndroidAuto(new CarDistance(1.5, CarDistanceUnit.Kilometers)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitMiles, CarDistanceUnits.AndroidAuto(new CarDistance(12, CarDistanceUnit.Miles)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitMilesP1, CarDistanceUnits.AndroidAuto(new CarDistance(1.5, CarDistanceUnit.Miles)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitFeet, CarDistanceUnits.AndroidAuto(new CarDistance(1, CarDistanceUnit.Feet)).Unit);
        Check.Equal(AndroidX.Car.App.Model.Distance.UnitYards, CarDistanceUnits.AndroidAuto(new CarDistance(1, CarDistanceUnit.Yards)).Unit);
        return Task.CompletedTask;
    }
}
