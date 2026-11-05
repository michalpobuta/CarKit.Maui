namespace CarKit.Maui;

// POI rules on Android Auto (spec: "Category decisions (Stage 7)"), shared by the validator, renderers and FakeCarHost.
// PlaceListMapTemplate (the host map) is allowed only for apps with the POI category; other categories get a place list
// without a map, which has no refresh button and no distance requirement. A PlaceListMapTemplate row has no action or
// details card, so without OnSelected a click runs the primary action of the CarPlay card.
internal static class CarPointsOfInterest
{
    // PlaceListMapTemplate.Builder.setOnContentRefreshListener has @RequiresCarApi(5)
    public const int RefreshApiLevel = 5;

    public static bool UsesPlaceListMap(CarAppCategory categories) =>
        (categories & (CarAppCategory.Parking | CarAppCategory.EvCharging | CarAppCategory.Fueling)) != 0;

    public static (CarElement Element, Func<CarItemContext, Task>? Handler) AndroidAutoSelection(CarPointOfInterest point) =>
        point.OnSelected is { } selected ? (point, selected)
        : point.PrimaryAction is { IsEnabled: true, OnSelected: { } primary } action ? (action, primary)
        : (point, null);
}
