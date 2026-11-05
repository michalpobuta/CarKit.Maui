using CarKit.Maui.Core.Tests.Session;
using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Model;

// Stage 7: POI model, distance, map area and voice animations (spec: "Category decisions (Stage 7)")
internal static class P
{
    public static readonly CarLocation CityCenter = new(52.2297, 21.0122);

    public static CarPointOfInterest Point(string id, CarDistance? distance = null) => new(CityCenter, "Parking " + id)
    {
        Id = id,
        Distance = distance ?? new CarDistance(1.2, CarDistanceUnit.Kilometers),
    };

    public static CarPointOfInterestTemplate Map(params CarPointOfInterest[] points) => new("Parking lots") { Id = "map", Points = [.. points] };
}

public class PointOfInterestModelTests
{
    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Distance_rejects_negative_and_non_finite_values(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarDistance(value, CarDistanceUnit.Meters));

    [Fact]
    public void Distance_is_a_value()
    {
        Assert.Equal(new CarDistance(0, CarDistanceUnit.Miles), new CarDistance(0, CarDistanceUnit.Miles));
        Assert.NotEqual(new CarDistance(1, CarDistanceUnit.Miles), new CarDistance(1, CarDistanceUnit.Kilometers));
        Assert.Equal(350, new CarDistance(350, CarDistanceUnit.Meters).Value);
    }

    [Theory]
    [InlineData(-0.1, 0.1)]
    [InlineData(0.1, -0.1)]
    [InlineData(double.NaN, 0.1)]
    [InlineData(0.1, double.PositiveInfinity)]
    public void Map_region_rejects_negative_deltas(double latitudeDelta, double longitudeDelta) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarMapRegion(P.CityCenter, latitudeDelta, longitudeDelta));

    [Fact]
    public void Map_region_keeps_center_and_span()
    {
        var region = new CarMapRegion(P.CityCenter, 0.05, 0.08);

        Assert.Equal(P.CityCenter, region.Center);
        Assert.Equal(0.05, region.LatitudeDelta);
        Assert.Equal(0.08, region.LongitudeDelta);
    }

    [Fact]
    public void Voice_animation_duration_must_be_positive()
    {
        var state = new CarVoiceControlState("listening", "Listening");

        Assert.Equal(TimeSpan.FromSeconds(1), state.AnimationDuration);
        Assert.True(state.AnimationImages.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => state with { AnimationDuration = TimeSpan.Zero });
        Assert.Equal(TimeSpan.FromMilliseconds(600), (state with { AnimationDuration = TimeSpan.FromMilliseconds(600) }).AnimationDuration);
    }

    [Fact]
    public void Point_and_template_defaults()
    {
        var point = new CarPointOfInterest(P.CityCenter, "Parking");
        var template = new CarPointOfInterestTemplate("Parking lots");

        Assert.Null(point.Distance);
        Assert.Null(point.SelectedPinImage);
        Assert.False(template.IsLoading);
        Assert.Null(template.OnMapRegionChanged);
        Assert.Null(template.OnRefreshRequested);
    }

    [Fact]
    public void Android_auto_selection_prefers_on_selected_then_the_primary_action()
    {
        var primary = new CarAction("Details", _ => Task.CompletedTask);
        Func<CarItemContext, Task> selected = _ => Task.CompletedTask;
        var point = P.Point("a") with { PrimaryAction = primary };

        var (element, handler) = CarPointsOfInterest.AndroidAutoSelection(point);
        Assert.Same(primary, element);
        Assert.Same(primary.OnSelected, handler);

        var withHandler = point with { OnSelected = selected };
        (element, handler) = CarPointsOfInterest.AndroidAutoSelection(withHandler);
        Assert.Same(withHandler, element);
        Assert.Same(selected, handler);

        Assert.Null(CarPointsOfInterest.AndroidAutoSelection(point with { PrimaryAction = primary with { IsEnabled = false } }).Handler);
        Assert.Null(CarPointsOfInterest.AndroidAutoSelection(P.Point("b")).Handler);
    }
}

public class PointOfInterestHostTests
{
    [Fact]
    public async Task Map_region_change_runs_the_handler_on_the_main_thread()
    {
        var calls = new List<string>();
        FakeCarHost? host = null;
        var map = P.Map(P.Point("a")) with
        {
            OnMapRegionChanged = context =>
            {
                calls.Add(FormattableString.Invariant($"{context.Element.Id}:{context.Region.LatitudeDelta}:{!host!.Dispatcher.IsDispatchRequired}"));
                return Task.CompletedTask;
            },
        };
        (host, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.CarPlay, map);

        await host.ChangeMapRegionAsync(new CarMapRegion(P.CityCenter, 0.25, 0.25));

        Assert.Equal(["map:0.25:True"], calls);
        var (androidAuto, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.AndroidAuto, map);
        await Assert.ThrowsAsync<InvalidOperationException>(() => androidAuto.ChangeMapRegionAsync(new CarMapRegion(P.CityCenter, 1, 1)));
        var (list, _) = await S.ConnectWithRoot(CarAppCategory.Parking);
        await Assert.ThrowsAsync<InvalidOperationException>(() => list.ChangeMapRegionAsync(new CarMapRegion(P.CityCenter, 1, 1)));
    }

    [Fact]
    public async Task Refresh_runs_the_handler_on_android_auto()
    {
        var calls = new List<string>();
        var map = P.Map(P.Point("a")) with
        {
            OnRefreshRequested = context =>
            {
                calls.Add(context.Element.Id);
                return Task.CompletedTask;
            },
        };
        var (host, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.AndroidAuto, map);

        await host.RefreshAsync();

        Assert.Equal(["map"], calls);
        var (carPlay, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.CarPlay, map);
        await Assert.ThrowsAsync<InvalidOperationException>(carPlay.RefreshAsync);
    }

    [Fact]
    public async Task Selecting_a_point_on_android_auto_without_handler_runs_the_primary_action()
    {
        var calls = new List<string>();
        var point = P.Point("a") with
        {
            PrimaryAction = new CarAction("Details", context =>
            {
                calls.Add("primary:" + context.Element.Id);
                return Task.CompletedTask;
            })
            {
                Id = "details",
            },
        };
        var (androidAuto, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.AndroidAuto, P.Map(point));
        var (carPlay, _) = await S.ConnectWithRoot(CarAppCategory.Parking, CarPlatform.CarPlay, P.Map(point));

        await androidAuto.SelectAsync(point);
        await carPlay.SelectAsync(point);

        Assert.Equal(["primary:details"], calls);
    }
}

public class VoiceAnimationFallbackTests
{
    [Fact]
    public void Android_auto_voice_fallback_shows_the_image_or_the_first_frame()
    {
        var first = CarImage.FromSystemSymbol("mic.fill", "ic_mic_1");
        var image = CarImage.FromSystemSymbol("mic", "ic_mic");
        var animated = new CarVoiceControlState("listening", "Listening") { AnimationImages = [first, CarImage.FromSystemSymbol("mic", "ic_mic_2")] };
        var validator = Validation.V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto);

        var message = Assert.IsType<CarMessageTemplate>(validator.Prepare(new CarVoiceControlTemplate([animated]), null, depthIfScreen: 2));
        var withImage = Assert.IsType<CarMessageTemplate>(validator.Prepare(new CarVoiceControlTemplate([animated with { Image = image }]), null, depthIfScreen: 2));

        Assert.Equal(first, message.Icon);
        Assert.Equal(image, withImage.Icon);
    }
}

// Final review I1/M3: refresh button and distance requirement only where Android Auto shows a PlaceListMapTemplate
// (POI categories; refresh since Car App API 5): a place list without a map (weather, navigation) has neither
public class PlaceListMapRulesTests
{
    [Fact]
    public async Task Refresh_is_only_available_on_the_place_list_map_from_api_5()
    {
        var calls = 0;
        var map = P.Map(P.Point("a")) with
        {
            OnRefreshRequested = _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
        };
        var (weather, _) = await S.ConnectWithRoot(CarAppCategory.Weather, CarPlatform.AndroidAuto, map);
        var oldHost = new FakeCarHost(CarAppCategory.Parking, CarPlatform.AndroidAuto) { CarAppApiLevel = 4 };
        await oldHost.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(map)));

        var weatherError = await Assert.ThrowsAsync<InvalidOperationException>(weather.RefreshAsync);
        var oldHostError = await Assert.ThrowsAsync<InvalidOperationException>(oldHost.RefreshAsync);

        Assert.Contains("without a map", weatherError.Message, StringComparison.Ordinal);
        Assert.Contains("API level 5", oldHostError.Message, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Point_without_distance_is_accepted_in_the_list_without_a_map()
    {
        var map = P.Map(new CarPointOfInterest(P.CityCenter, "Measuring station") { Id = "station" });

        Assert.Same(map, Validation.V.Create(CarAppCategory.Weather, CarPlatform.AndroidAuto).Prepare(map, 1));
        Assert.Same(map, Validation.V.Create(CarAppCategory.Navigation, CarPlatform.AndroidAuto).Prepare(map, 1));
        Assert.Throws<CarKitException>(() => Validation.V.Create(CarAppCategory.Weather | CarAppCategory.Fueling, CarPlatform.AndroidAuto).Prepare(map, 1));
    }

    [Theory]
    [InlineData(CarAppCategory.Parking, true)]
    [InlineData(CarAppCategory.EvCharging | CarAppCategory.Navigation, true)]
    [InlineData(CarAppCategory.Weather | CarAppCategory.Navigation, false)]
    public void Place_list_map_follows_the_poi_categories(CarAppCategory categories, bool expected) =>
        Assert.Equal(expected, CarPointsOfInterest.UsesPlaceListMap(categories));
}
