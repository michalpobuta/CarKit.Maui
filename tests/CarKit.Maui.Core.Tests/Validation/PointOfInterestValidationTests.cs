using CarKit.Maui.Core.Tests.Model;
using Xunit;

namespace CarKit.Maui.Core.Tests.Validation;

// Stage 7: POI data without which the host rejects the template, and point limits (spec: "Category decisions (Stage 7)")
public class PointOfInterestValidationTests
{
    [Fact]
    public void Point_without_distance_is_rejected_on_android_auto()
    {
        var map = P.Map(P.Point("a"), new CarPointOfInterest(P.CityCenter, "No distance") { Id = "b" });

        var ex = Assert.Throws<CarKitException>(() => V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto).Prepare(map, 1));

        Assert.Contains("'b'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Distance", ex.Message, StringComparison.Ordinal);
        Assert.Same(map, V.Create(CarAppCategory.Parking).Prepare(map, 1));
        Assert.NotNull(V.Create(CarAppCategory.Weather, CarPlatform.AndroidAuto, configure: o => o.Validation = CarValidationMode.Warn).Prepare(map, 1));
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public void Unknown_selected_point_is_rejected(CarPlatform platform)
    {
        var map = P.Map(P.Point("a"), P.Point("b"));
        var validator = V.Create(CarAppCategory.Parking, platform);

        var selected = map with { SelectedPointId = "b" };
        Assert.Same(selected, validator.Prepare(selected, 1));
        var ex = Assert.Throws<CarKitException>(() => validator.Prepare(map with { SelectedPointId = "c" }, 1));
        Assert.Contains("SelectedPointId 'c'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Points_over_the_limit_are_truncated_or_rejected()
    {
        var thirteen = P.Map([.. Enumerable.Range(1, 13).Select(i => P.Point("p" + i))]);

        var carPlay = Assert.IsType<CarPointOfInterestTemplate>(V.Create(CarAppCategory.Parking).Prepare(thirteen, 1));
        Assert.Equal(12, carPlay.Points.Count);
        Assert.Equal("p12", carPlay.Points[^1].Id);

        var androidAuto = Assert.IsType<CarPointOfInterestTemplate>(V.Create(CarAppCategory.EvCharging, CarPlatform.AndroidAuto).Prepare(thirteen, 1));
        Assert.Equal(6, androidAuto.Points.Count);
        var host = Assert.IsType<CarPointOfInterestTemplate>(
            V.Create(CarAppCategory.Fueling, CarPlatform.AndroidAuto, runtimeLimits: kind => kind == CarLimitKind.PointsOfInterest ? 10 : null).Prepare(thirteen, 1));
        Assert.Equal(10, host.Points.Count);

        var ex = Assert.Throws<CarTemplateLimitException>(() =>
            V.Create(CarAppCategory.Parking, configure: o => o.Limits.OnOverflow = CarOverflowBehavior.Throw).Prepare(thirteen, 1));
        Assert.Equal(CarLimitKind.PointsOfInterest, ex.Limit);
        Assert.Equal(12, ex.Maximum);
        Assert.Equal(13, ex.Actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Android_auto_poi_is_supported_from_api_1(int apiLevel)
    {
        var map = P.Map(P.Point("a"));

        Assert.Same(map, V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto, apiLevel: apiLevel).Prepare(map, 1));
    }

    [Fact]
    public void Points_in_a_carplay_tab_are_checked()
    {
        var tabs = new CarTabTemplate([P.Map(P.Point("a")) with { SelectedPointId = "x", TabInfo = new CarTabInfo("Map") }]);

        Assert.Throws<CarKitException>(() => V.Create(CarAppCategory.Parking).Prepare(tabs, 1));
    }
}
