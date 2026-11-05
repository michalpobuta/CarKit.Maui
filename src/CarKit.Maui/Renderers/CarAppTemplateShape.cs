namespace CarKit.Maui.Renderers;

// What the Car App Library host compares to decide whether a screen's new template is a refresh (no task step).
// The shape is computed from the native object (Platforms/Android/Renderers/CarAppShapes), so it describes what was
// actually sent.
internal sealed record CarAppTemplateShape(string Type, string? Title, bool IsLoading)
{
    public CarArray<CarAppSectionShape> Sections { get; init; }

    public string? Message { get; init; }

    public CarArray<CarAppTabShape> Tabs { get; init; }

    public string? ActiveTab { get; init; }

    public CarAppTemplateShape? ActiveContent { get; init; }
}

internal sealed record CarAppSectionShape(string? Title, CarArray<CarAppRowShape> Rows);

// Toggle: the row's switch state (null = none); a state change allows a title change without losing the refresh
internal sealed record CarAppRowShape(string Title, bool? Toggle = null);

internal sealed record CarAppTabShape(string Title, string? Icon);

// Car App Library template class names
internal static class CarAppTemplateTypes
{
    public const string List = "ListTemplate";
    public const string Sectioned = "SectionedItemTemplate";
    public const string Grid = "GridTemplate";
    public const string Pane = "PaneTemplate";
    public const string Message = "MessageTemplate";
    public const string LongMessage = "LongMessageTemplate";
    public const string Search = "SearchTemplate";
    public const string Tab = "TabTemplate";
    public const string SignIn = "SignInTemplate";
    public const string MediaPlayback = "MediaPlaybackTemplate";
    public const string Navigation = "NavigationTemplate";
    public const string PlaceListMap = "PlaceListMapTemplate";

    // Stage 8 (Navigation module): route preview and POI with the app map (API 7), route preview below API 7 (deprecated)
    public const string MapWithContent = "MapWithContentTemplate";
    public const string RoutePreview = "RoutePreviewNavigationTemplate";

    // The type CarAppTemplateRenderers will build for the model (the List/Sectioned and PlaceListMap/List choice is fixed
    // per connection)
    public static string Of(CarTemplate template, bool useSectionedItemTemplate, bool usePlaceListMap = true) => template switch
    {
        CarListTemplate => useSectionedItemTemplate ? Sectioned : List,
        CarPointOfInterestTemplate => usePlaceListMap ? PlaceListMap : List,
        CarGridTemplate => Grid,
        CarTabTemplate => Tab,
        CarInformationTemplate => Pane,
        CarSearchTemplate => Search,
        CarMessageTemplate => Message,
        CarLongMessageTemplate => LongMessage,
        AndroidAutoSignInTemplate => SignIn,
        CarNowPlayingTemplate => MediaPlayback,
        // Without the Navigation module (which supplies the type by navigation state): NavigationTemplate
        CarMapTemplate => Navigation,
        _ => template.Kind.ToString(),
    };
}
