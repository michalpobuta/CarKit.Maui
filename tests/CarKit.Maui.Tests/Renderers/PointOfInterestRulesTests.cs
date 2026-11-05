using CarKit.Maui.Renderers;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

// Stage 7: choosing the POI template on Android Auto, distance units, refreshes, steps and the CarPlay update plan
// (spec: "Category decisions (Stage 7)")
public class PointOfInterestRulesTests
{
    private static readonly CarLocation _cityCenter = new(52.23, 21.01);

    private static CarPointOfInterestTemplate Map(params string[] titles) => new("Parking lots")
    {
        Id = "map",
        Points = [.. titles.Select(title => new CarPointOfInterest(_cityCenter, title) { Id = title })],
    };

    private static CarAppTemplateShape PlaceList(string title, params string[] rows) =>
        new(CarAppTemplateTypes.PlaceListMap, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, [.. rows.Select(r => new CarAppRowShape(r))])] };

    [Theory]
    [InlineData(CarAppCategory.Parking, true)]
    [InlineData(CarAppCategory.EvCharging, true)]
    [InlineData(CarAppCategory.Fueling, true)]
    [InlineData(CarAppCategory.Parking | CarAppCategory.Navigation, true)]
    [InlineData(CarAppCategory.Navigation, false)]
    [InlineData(CarAppCategory.Weather, false)]
    [InlineData(CarAppCategory.Iot | CarAppCategory.Weather, false)]
    public void Place_list_map_only_for_poi_categories(CarAppCategory categories, bool expected) =>
        Assert.Equal(expected, CarRenderSwitches.UsePlaceListMap(categories));

    [Fact]
    public void Poi_type_follows_the_connection_choice()
    {
        var map = Map("A");

        Assert.Equal(CarAppTemplateTypes.PlaceListMap, CarAppTemplateTypes.Of(map, useSectionedItemTemplate: true));
        Assert.Equal(CarAppTemplateTypes.PlaceListMap, CarAppTemplateTypes.Of(map, useSectionedItemTemplate: false, usePlaceListMap: true));
        Assert.Equal(CarAppTemplateTypes.List, CarAppTemplateTypes.Of(map, useSectionedItemTemplate: true, usePlaceListMap: false));
        Assert.Equal(CarAppTemplateTypes.Sectioned, CarAppTemplateTypes.Of(new CarListTemplate("List"), useSectionedItemTemplate: true, usePlaceListMap: false));
    }

    // Distance.UNIT_* constants from the Car App Library:
    // METERS 1, KILOMETERS 2, KILOMETERS_P1 3, MILES 4, MILES_P1 5, FEET 6, YARDS 7
    [Theory]
    [InlineData(350, CarDistanceUnit.Meters, 350, 1)]
    [InlineData(12, CarDistanceUnit.Kilometers, 12, 2)]
    [InlineData(1.25, CarDistanceUnit.Kilometers, 1.25, 3)]
    [InlineData(4, CarDistanceUnit.Kilometers, 4, 2)]
    [InlineData(15.5, CarDistanceUnit.Kilometers, 15.5, 2)]
    [InlineData(0.8, CarDistanceUnit.Miles, 0.8, 5)]
    [InlineData(20, CarDistanceUnit.Miles, 20, 4)]
    [InlineData(500, CarDistanceUnit.Feet, 500, 6)]
    [InlineData(90, CarDistanceUnit.Yards, 90, 7)]
    public void Distance_units_map_to_car_app_constants(double value, CarDistanceUnit unit, double expectedValue, int expectedUnit) =>
        Assert.Equal((expectedValue, expectedUnit), CarDistanceUnits.AndroidAuto(new CarDistance(value, unit)));

    // Review Focus 5: PlaceListMapTemplate javadoc: a refresh when the previous one was loading, or the title, the number of
    // rows and the row titles are the same (distance, subtitle and summary are not in the shape)
    [Fact]
    public void Poi_shape_refresh_follows_place_list_rules()
    {
        var loading = new CarAppTemplateShape(CarAppTemplateTypes.PlaceListMap, "Parking lots", IsLoading: true);

        Assert.True(CarAppRefreshRules.IsRefresh(PlaceList("Parking lots", "A", "B"), PlaceList("Parking lots", "A", "B")));
        Assert.True(CarAppRefreshRules.IsRefresh(loading, PlaceList("Parking lots", "A", "B", "C")));
        Assert.False(CarAppRefreshRules.IsRefresh(PlaceList("Parking lots", "A", "B"), PlaceList("Parking lots", "A", "B", "C")));
        Assert.False(CarAppRefreshRules.IsRefresh(PlaceList("Parking lots", "A", "B"), PlaceList("Chargers", "A", "B")));
        Assert.False(CarAppRefreshRules.IsRefresh(PlaceList("Parking lots", "A"), PlaceList("Parking lots", "A") with { Type = CarAppTemplateTypes.List }));
    }

    [Fact]
    public void Place_list_map_costs_a_step_and_does_not_end_the_task()
    {
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.PlaceListMap));
        Assert.True(CarAppTaskSteps.Allows(4, CarAppTemplateTypes.PlaceListMap));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.PlaceListMap));
    }

    [Fact]
    public void Poi_updates_in_place()
    {
        var map = Map("A", "B");

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(map, map with { Title = "Other title" }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(map, Map("C") with { SelectedPointId = "C" }));
    }
}
