namespace CarKit.Maui.Renderers;

// Renderer AppContext switches (RuntimeHostConfigurationOption in the app project)
internal static class CarRenderSwitches
{
    // false = ListTemplate also on hosts with Car App API 8 (SectionedItemTemplate is @ExperimentalCarApi in 1.7.0)
    public const string SectionedItemTemplate = "CarKit.Maui.Android.SectionedItemTemplate";

    public const int SectionedItemTemplateApiLevel = 8;

    // Stage 6: apps with the Communication category always use ListTemplate — RowSection accepts only Row, and
    // conversation rows are ConversationItem (the choice comes from the category, not from the list content, so it is
    // fixed per connection)
    public static bool UseSectionedItemTemplate(int apiLevel, bool? switchValue, CarAppCategory categories = CarAppCategory.None) =>
        apiLevel >= SectionedItemTemplateApiLevel && switchValue != false && !categories.HasFlag(CarAppCategory.Communication);

    // Stage 7: PlaceListMapTemplate (host map) only for apps with a POI category; other categories with POI in the rules
    // (Navigation, Weather) get a list of places without a map (ListTemplate) until the app map in Stage 8
    public static bool UsePlaceListMap(CarAppCategory categories) => CarPointsOfInterest.UsesPlaceListMap(categories);
}
