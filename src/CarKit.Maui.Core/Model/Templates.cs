using System.Diagnostics.CodeAnalysis;

namespace CarKit.Maui;

/// <summary>A list with sections (CarPlay <c>CPListTemplate</c>, Android Auto <c>ListTemplate</c>).</summary>
public sealed record CarListTemplate : CarTemplate
{
    /// <summary>Creates the list.</summary>
    /// <param name="title">The title.</param>
    public CarListTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.List;

    /// <summary>Gets the sections.</summary>
    public CarArray<CarSection> Sections { get; init; }

    /// <summary>Gets the title shown when the list is empty.</summary>
    public CarText? EmptyViewTitle { get; init; }

    /// <summary>Gets the subtitle shown when the list is empty.</summary>
    public CarText? EmptyViewSubtitle { get; init; }

    /// <summary>Gets a value indicating whether the list shows a loading state (Android Auto).</summary>
    public bool IsLoading { get; init; }

    /// <summary>
    /// Gets the details header above the sections (CarPlay iOS 26.4; <see cref="CarFeature.ListDetailsHeader"/>). Android Auto
    /// and older CarPlay show the list without it.
    /// </summary>
    public CarListDetailsHeader? DetailsHeader { get; init; }
}

/// <summary>A grid of buttons (CarPlay <c>CPGridTemplate</c>, Android Auto <c>GridTemplate</c>).</summary>
public sealed record CarGridTemplate : CarTemplate
{
    /// <summary>Creates the grid.</summary>
    /// <param name="title">The title.</param>
    public CarGridTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Grid;

    /// <summary>Gets the buttons.</summary>
    public CarArray<CarGridItem> Items { get; init; }

    /// <summary>Gets a value indicating whether the grid shows a loading state (Android Auto).</summary>
    public bool IsLoading { get; init; }
}

/// <summary>Tabs, each holding another template (CarPlay <c>CPTabBarTemplate</c>, Android Auto <c>TabTemplate</c>).</summary>
public sealed record CarTabTemplate : CarTemplate
{
    /// <summary>Creates the tabs.</summary>
    /// <param name="tabs">The tab templates; describe each with <see cref="CarTemplate.TabInfo"/>.</param>
    public CarTabTemplate(IEnumerable<CarTemplate> tabs) =>
        Tabs = tabs?.ToCarArray() ?? throw new ArgumentNullException(nameof(tabs));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Tab;

    /// <summary>Gets the tab templates.</summary>
    public CarArray<CarTemplate> Tabs { get; init; }

    /// <summary>Gets the identifier of the selected tab.</summary>
    public string? SelectedTabId { get; init; }

    /// <summary>Gets the handler called when the user selects a tab.</summary>
    public Func<CarTabContext, Task>? OnTabSelected { get; init; }
}

/// <summary>Label and value pairs with actions (CarPlay <c>CPInformationTemplate</c>, Android Auto <c>PaneTemplate</c>).</summary>
public sealed record CarInformationTemplate : CarTemplate
{
    /// <summary>Creates the panel.</summary>
    /// <param name="title">The title.</param>
    public CarInformationTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Information;

    /// <summary>Gets the label and value pairs.</summary>
    public CarArray<CarInformationItem> Items { get; init; }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }

    /// <summary>Gets the layout on CarPlay.</summary>
    public CarInformationLayout Layout { get; init; }

    /// <summary>Gets a value indicating whether the panel shows a loading state (Android Auto).</summary>
    public bool IsLoading { get; init; }
}

/// <summary>Search with results (CarPlay <c>CPSearchTemplate</c>, Android Auto <c>SearchTemplate</c>).</summary>
public sealed record CarSearchTemplate : CarTemplate
{
    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Search;

    /// <summary>Gets the hint shown in the empty search field.</summary>
    public CarText? Hint { get; init; }

    /// <summary>Gets the initial search text.</summary>
    public string? InitialText { get; init; }

    /// <summary>Gets a value indicating whether the keyboard opens immediately (Android Auto).</summary>
    public bool ShowKeyboardByDefault { get; init; } = true;

    /// <summary>Gets the handler that returns results for the current text.</summary>
    public Func<CarSearchContext, Task<IReadOnlyList<CarListItem>>>? OnSearchTextChanged { get; init; }

    /// <summary>Gets the handler called when the user presses search.</summary>
    public Func<CarSearchContext, Task>? OnSearchSubmitted { get; init; }
}

/// <summary>
/// Places on a map with detail cards. CarPlay shows a <c>CPPointOfInterestTemplate</c> (at most 12 places). Android Auto
/// shows a <c>PlaceListMapTemplate</c> with a map drawn by the host when the app declares a point-of-interest category
/// (Parking, EV charging, fueling), and a list of places without a map in other categories.
/// </summary>
public sealed record CarPointOfInterestTemplate : CarTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="title">The title.</param>
    public CarPointOfInterestTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.PointOfInterest;

    /// <summary>Gets the places. On Android Auto every place needs a <see cref="CarPointOfInterest.Distance"/>.</summary>
    public CarArray<CarPointOfInterest> Points { get; init; }

    /// <summary>Gets the identifier of the selected place (CarPlay).</summary>
    public string? SelectedPointId { get; init; }

    /// <summary>Gets a value indicating whether the places are loading (Android Auto).</summary>
    public bool IsLoading { get; init; }

    /// <summary>
    /// Gets the handler called when the driver pans or zooms the map (CarPlay). Update the template with the places in
    /// <see cref="CarMapRegionContext.Region"/>.
    /// </summary>
    public Func<CarMapRegionContext, Task>? OnMapRegionChanged { get; init; }

    /// <summary>
    /// Gets the handler called when the driver presses the refresh button (Android Auto). The button appears only when the
    /// handler is set, the host has Car App API level 5 or later and the app declares a point-of-interest category (the
    /// place list with a map); the list without a map has no button, and CarKit logs a warning. Update the template from
    /// the handler without <see cref="IsLoading"/>: a loading template is a new task step, not a refresh.
    /// </summary>
    public Func<CarItemContext, Task>? OnRefreshRequested { get; init; }
}

/// <summary>A short message with an icon and up to two actions (Android Auto <c>MessageTemplate</c>).</summary>
public sealed record CarMessageTemplate : CarTemplate
{
    /// <summary>Creates the message.</summary>
    /// <param name="message">The message.</param>
    public CarMessageTemplate(CarText message) => Message = message;

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Message;

    /// <summary>Gets the message.</summary>
    public CarText Message { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the icon.</summary>
    public CarImage? Icon { get; init; }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }

    /// <summary>Gets a value indicating whether a loading indicator is shown.</summary>
    public bool IsLoading { get; init; }
}

/// <summary>The now-playing screen (module Media).</summary>
public sealed record CarNowPlayingTemplate : CarTemplate
{
    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.NowPlaying;

    /// <summary>Gets the playback buttons.</summary>
    public CarArray<CarNowPlayingButton> Buttons { get; init; }

    /// <summary>Gets a value indicating whether the Up Next button is shown.</summary>
    public bool IsUpNextButtonEnabled { get; init; }

    /// <summary>Gets the Up Next button title.</summary>
    public CarText? UpNextTitle { get; init; }

    /// <summary>Gets a value indicating whether the album or artist button is shown.</summary>
    public bool IsAlbumArtistButtonEnabled { get; init; }

    /// <summary>Gets the Up Next handler.</summary>
    public Func<CarItemContext, Task>? OnUpNextSelected { get; init; }

    /// <summary>Gets the album or artist handler.</summary>
    public Func<CarItemContext, Task>? OnAlbumArtistSelected { get; init; }

    /// <summary>
    /// Gets the sports mode: teams, status and clock instead of the artwork (CarPlay iOS 18.4;
    /// <see cref="CarFeature.NowPlayingSportsMode"/>). Android Auto shows the regular screen.
    /// </summary>
    public CarNowPlayingSports? Sports { get; init; }

    /// <summary>
    /// Gets a value indicating whether CarPlay may show the MiniPlayer (iOS 27, <c>true</c> by default). Current CarKit builds
    /// cannot hide it (<see cref="CarFeature.NowPlayingMiniPlayerOptOut"/>); <see langword="false"/> is logged and ignored.
    /// </summary>
    [Experimental("CARKIT0001")]
    public bool AllowsMiniPlayer { get; init; } = true;
}

/// <summary>
/// A map drawn by the app with turn-by-turn guidance (module Navigation): CarPlay <c>CPMapTemplate</c> over the car window,
/// Android Auto <c>NavigationTemplate</c> (route previews: <c>MapWithContentTemplate</c>) over the app's surface. The
/// guidance comes from <see cref="ICarNavigationSession"/>; Android Auto needs at least one of
/// <see cref="CarTemplate.TrailingBarButtons"/> for the template's action strip.
/// </summary>
public sealed record CarMapTemplate : CarTemplate
{
    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Map;

    /// <summary>Gets the buttons drawn over the map (at most 4; Android Auto: 3 with <see cref="IsPanningEnabled"/>).</summary>
    public CarArray<CarMapButton> MapButtons { get; init; }

    /// <summary>
    /// Gets a value indicating whether the driver can pan the map. Android Auto adds its pan button to the map buttons and
    /// sends scroll gestures only in pan mode; CarPlay sends pan gestures without it.
    /// </summary>
    public bool IsPanningEnabled { get; init; }

    /// <summary>Gets the title of the button that starts the previewed trip (CarPlay: system title when not set; Android Auto: "Start").</summary>
    public CarText? StartTripTitle { get; init; }

    /// <summary>Gets the guidance panel background color (CarPlay).</summary>
    public CarColor? GuidanceBackgroundColor { get; init; }

    /// <summary>Gets a value indicating whether the navigation bar hides automatically (CarPlay).</summary>
    public bool AutomaticallyHidesNavigationBar { get; init; } = true;
}

/// <summary>A long text shown only when parked (Android Auto <c>LongMessageTemplate</c>).</summary>
public sealed record CarLongMessageTemplate : CarTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    public CarLongMessageTemplate(CarText title, string message)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message;
    }

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.LongMessage;

    /// <summary>Gets the message.</summary>
    public string Message { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }
}

/// <summary>A contact card with call, message and directions buttons (CarPlay only).</summary>
public sealed record CarPlayContactTemplate : CarTemplate
{
    /// <summary>Creates the contact card.</summary>
    /// <param name="name">The contact name.</param>
    public CarPlayContactTemplate(CarText name) => Name = name;

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.CarPlayContact;

    /// <summary>Gets the contact name.</summary>
    public CarText Name { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the subtitle.</summary>
    public CarText? Subtitle { get; init; }

    /// <summary>Gets the informative text.</summary>
    public CarText? InformativeText { get; init; }

    /// <summary>Gets the contact image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the buttons.</summary>
    public CarArray<CarPlayContactButton> Buttons { get; init; }
}

/// <summary>A sign-in screen shown only when parked (Android Auto only).</summary>
public sealed record AndroidAutoSignInTemplate : CarTemplate
{
    /// <summary>Creates the sign-in screen.</summary>
    /// <param name="title">The title.</param>
    /// <param name="method">The sign-in method.</param>
    public AndroidAutoSignInTemplate(CarText title, AndroidAutoSignInMethod method)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Method = method;
    }

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.AndroidAutoSignIn;

    /// <summary>Gets the sign-in method.</summary>
    public AndroidAutoSignInMethod Method { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the instructions.</summary>
    public CarText? Instructions { get; init; }

    /// <summary>Gets the additional text, such as terms.</summary>
    public CarText? AdditionalText { get; init; }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }

    /// <summary>Gets a value indicating whether a loading state is shown.</summary>
    public bool IsLoading { get; init; }
}

/// <summary>
/// Video browsing on CarPlay (iOS 27, category Video, parked only): a list with a details header whose rows carry
/// <see cref="CarListItem.Playback"/> with <see cref="CarPlaybackPresentation.Video"/>; the app plays video over AirPlay.
/// CarPlay renders it as a <c>CPListTemplate</c>. Experimental until verified with the iOS 27 SDK and a car with video.
/// </summary>
[Experimental("CARKIT0001")]
public sealed record CarPlayVideoBrowseTemplate : CarTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="title">The title.</param>
    public CarPlayVideoBrowseTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.CarPlayVideoBrowse;

    /// <summary>Gets the sections.</summary>
    public CarArray<CarSection> Sections { get; init; }

    /// <summary>Gets the details header above the sections, such as the featured video.</summary>
    public CarListDetailsHeader? DetailsHeader { get; init; }
}

/// <summary>An alert (CarPlay <c>CPAlertTemplate</c>, Android Auto <c>Alert</c>).</summary>
public sealed record CarAlertTemplate : CarModalTemplate
{
    /// <summary>Creates the alert.</summary>
    /// <param name="title">The title; add shorter variants for small screens.</param>
    public CarAlertTemplate(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.Alert;

    /// <summary>Gets the subtitle (Android Auto).</summary>
    public CarText? Subtitle { get; init; }

    /// <summary>Gets the icon (Android Auto).</summary>
    public CarImage? Icon { get; init; }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }

    /// <summary>Gets how long the alert stays visible (Android Auto).</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>An action sheet (CarPlay <c>CPActionSheetTemplate</c>; a message screen on Android Auto).</summary>
public sealed record CarActionSheetTemplate : CarModalTemplate
{
    /// <summary>Creates the action sheet.</summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    public CarActionSheetTemplate(CarText? title, CarText? message)
    {
        Title = title;
        Message = message;
    }

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.ActionSheet;

    /// <summary>Gets the message.</summary>
    public CarText? Message { get; init; }

    /// <summary>Gets the actions.</summary>
    public CarArray<CarAction> Actions { get; init; }
}

/// <summary>Voice interaction states (CarPlay <c>CPVoiceControlTemplate</c>; a message screen on Android Auto).</summary>
public sealed record CarVoiceControlTemplate : CarModalTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="states">The states; the first is active unless <see cref="ActiveStateId"/> is set.</param>
    public CarVoiceControlTemplate(IEnumerable<CarVoiceControlState> states) =>
        States = states?.ToCarArray() ?? throw new ArgumentNullException(nameof(states));

    /// <inheritdoc />
    public override CarTemplateKind Kind => CarTemplateKind.VoiceControl;

    /// <summary>Gets the states.</summary>
    public CarArray<CarVoiceControlState> States { get; init; }

    /// <summary>Gets the identifier of the active state.</summary>
    public string? ActiveStateId { get; init; }

    /// <summary>
    /// Gets a value indicating whether CarPlay shows the template over the current template, such as the map (iOS 27).
    /// Current CarKit builds present it as a regular modal (<see cref="CarFeature.VoiceControlOverlay"/>).
    /// </summary>
    [Experimental("CARKIT0001")]
    public bool IsOverlay { get; init; }
}
