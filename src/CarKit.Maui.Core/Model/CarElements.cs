namespace CarKit.Maui;

/// <summary>Visual style of a <see cref="CarAction"/>.</summary>
public enum CarActionStyle
{
    /// <summary>The platform default.</summary>
    Default,

    /// <summary>The primary, highlighted action.</summary>
    Primary,

    /// <summary>An action that cancels the flow.</summary>
    Cancel,

    /// <summary>An action that deletes or discards data.</summary>
    Destructive,
}

/// <summary>A button in an alert, action sheet, information panel or message.</summary>
public sealed record CarAction : CarElement
{
    /// <summary>Creates the action.</summary>
    /// <param name="title">The button title.</param>
    /// <param name="onSelected">Called when the user selects the action.</param>
    public CarAction(CarText title, Func<CarItemContext, Task>? onSelected = null)
    {
        Title = title;
        OnSelected = onSelected;
    }

    /// <summary>Gets the button title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the optional icon.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the visual style.</summary>
    public CarActionStyle Style { get; init; }

    /// <summary>Gets a value indicating whether the action can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>A navigation bar button (CarPlay) or header action (Android Auto).</summary>
public sealed record CarBarButton : CarElement
{
    /// <summary>Creates a text button.</summary>
    /// <param name="title">The title.</param>
    public CarBarButton(CarText title) => Title = title ?? throw new ArgumentNullException(nameof(title));

    /// <summary>Creates an image button.</summary>
    /// <param name="image">The image.</param>
    public CarBarButton(CarImage image) => Image = image ?? throw new ArgumentNullException(nameof(image));

    /// <summary>Gets the title.</summary>
    public CarText? Title { get; init; }

    /// <summary>Gets the image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets a value indicating whether the button can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>A button drawn over the map.</summary>
public sealed record CarMapButton : CarElement
{
    /// <summary>Creates the button.</summary>
    /// <param name="image">The image.</param>
    public CarMapButton(CarImage image) => Image = image;

    /// <summary>Gets the image.</summary>
    public CarImage Image { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the image shown when the button has focus.</summary>
    public CarImage? FocusedImage { get; init; }

    /// <summary>Gets a value indicating whether the button is hidden.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Gets a value indicating whether the button can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>A button in a <see cref="CarGridTemplate"/>.</summary>
public sealed record CarGridItem : CarElement
{
    /// <summary>Creates the item.</summary>
    /// <param name="title">The title.</param>
    /// <param name="image">The image.</param>
    public CarGridItem(CarText title, CarImage image)
    {
        Title = title;
        Image = image;
    }

    /// <summary>Gets the title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the image.</summary>
    public CarImage Image { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the secondary text (Android Auto).</summary>
    public CarText? Text { get; init; }

    /// <summary>Gets a value indicating whether the item can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>Accessory shown at the end of a <see cref="CarListItem"/>.</summary>
public enum CarListItemAccessory
{
    /// <summary>No accessory.</summary>
    None,

    /// <summary>A disclosure indicator.</summary>
    DisclosureIndicator,

    /// <summary>A cloud (not downloaded) indicator.</summary>
    Cloud,
}

/// <summary>Base of the rows a <see cref="CarSection"/> can hold.</summary>
public abstract record CarListTemplateItem : CarElement
{
    private protected CarListTemplateItem()
    {
    }

    /// <summary>Gets a value indicating whether the row can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler. On CarPlay the row shows a spinner until the task completes.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>A list row.</summary>
public sealed record CarListItem : CarListTemplateItem
{
    /// <summary>Creates the row.</summary>
    /// <param name="text">The primary text.</param>
    public CarListItem(CarText text) => Text = text;

    /// <summary>Gets the primary text.</summary>
    public CarText Text { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the secondary text.</summary>
    public CarText? Detail { get; init; }

    /// <summary>Gets the leading image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the trailing accessory.</summary>
    public CarListItemAccessory Accessory { get; init; }

    /// <summary>Gets a value indicating whether selecting the row opens another list.</summary>
    public bool IsBrowsable { get; init; }

    /// <summary>Gets a value indicating whether the row shows the now-playing indicator.</summary>
    public bool IsPlaying { get; init; }

    /// <summary>Gets the playback progress from 0 to 1.</summary>
    public double? PlaybackProgress
    {
        get;
        init
        {
            if (value is { } progress && progress is not (>= 0 and <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), progress, "Playback progress must be between 0 and 1.");
            }

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether the content is explicit.</summary>
    public bool IsExplicitContent { get; init; }

    /// <summary>Gets the toggle shown in the row (Android Auto).</summary>
    public CarListItemToggle? Toggle { get; init; }

    /// <summary>Gets the place shown for the row on map templates (Android Auto).</summary>
    public CarLocation? Location { get; init; }

    /// <summary>
    /// Gets the playback state of the row (CarPlay iOS 26.4; <see cref="CarFeature.PlaybackConfiguration"/>). Use
    /// <see cref="CarPlaybackPresentation.Video"/> for video in <see cref="CarPlayVideoBrowseTemplate"/>.
    /// </summary>
    public CarPlaybackConfiguration? Playback { get; init; }
}

/// <summary>A toggle in a <see cref="CarListItem"/> (Android Auto).</summary>
/// <param name="IsChecked">Whether the toggle is on.</param>
public sealed record CarListItemToggle(bool IsChecked)
{
    /// <summary>Gets the handler called when the user flips the toggle.</summary>
    public Func<CarToggleContext, Task>? OnToggled { get; init; }
}

/// <summary>A row of images (CarPlay <c>CPListImageRowItem</c>).</summary>
public sealed record CarListImageRowItem : CarListTemplateItem
{
    /// <summary>Creates the row.</summary>
    /// <param name="text">The row title.</param>
    /// <param name="images">The images.</param>
    public CarListImageRowItem(CarText text, IEnumerable<CarImage> images)
    {
        Text = text;
        Images = images?.ToCarArray() ?? throw new ArgumentNullException(nameof(images));
    }

    /// <summary>Gets the row title.</summary>
    public CarText Text { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the images.</summary>
    public CarArray<CarImage> Images { get; init; }

    /// <summary>Gets the handler called when the user selects one image.</summary>
    public Func<CarImageRowContext, Task>? OnImageSelected { get; init; }
}

/// <summary>A section of a list.</summary>
public sealed record CarSection : CarElement
{
    /// <summary>Creates an empty section.</summary>
    public CarSection()
    {
    }

    /// <summary>Creates a section with rows.</summary>
    /// <param name="items">The rows.</param>
    public CarSection(IEnumerable<CarListTemplateItem> items) =>
        Items = items?.ToCarArray() ?? throw new ArgumentNullException(nameof(items));

    /// <summary>Gets the header.</summary>
    public CarText? Header { get; init; }

    /// <summary>Gets the title shown in the section index.</summary>
    public string? IndexTitle { get; init; }

    /// <summary>Gets the rows.</summary>
    public CarArray<CarListTemplateItem> Items { get; init; }
}

/// <summary>How a template appears as a tab of a <see cref="CarTabTemplate"/>.</summary>
public sealed record CarTabInfo
{
    /// <summary>Creates the tab description.</summary>
    /// <param name="title">The tab title.</param>
    /// <param name="image">The tab image.</param>
    public CarTabInfo(CarText title, CarImage? image = null)
    {
        Title = title;
        Image = image;
    }

    /// <summary>Gets the tab title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the tab image.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the badge value.</summary>
    public int? Badge { get; init; }
}

/// <summary>Layout of a <see cref="CarInformationTemplate"/> on CarPlay.</summary>
public enum CarInformationLayout
{
    /// <summary>Items aligned to the leading edge.</summary>
    Leading,

    /// <summary>Items in two columns.</summary>
    TwoColumn,
}

/// <summary>A label and value pair in a <see cref="CarInformationTemplate"/>.</summary>
public sealed record CarInformationItem
{
    /// <summary>Creates the item.</summary>
    /// <param name="title">The label.</param>
    /// <param name="detail">The value.</param>
    public CarInformationItem(CarText? title, CarText detail)
    {
        Title = title;
        Detail = detail;
    }

    /// <summary>Gets the label.</summary>
    public CarText? Title { get; init; }

    /// <summary>Gets the value.</summary>
    public CarText Detail { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }
}

/// <summary>A place on a <see cref="CarPointOfInterestTemplate"/>.</summary>
public sealed record CarPointOfInterest : CarElement
{
    /// <summary>Creates the place.</summary>
    /// <param name="location">The coordinate.</param>
    /// <param name="title">The title.</param>
    public CarPointOfInterest(CarLocation location, CarText title)
    {
        Location = location;
        Title = title;
    }

    /// <summary>Gets the coordinate.</summary>
    public CarLocation Location { get; init; }

    /// <summary>Gets the title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the subtitle.</summary>
    public CarText? Subtitle { get; init; }

    /// <summary>Gets the summary.</summary>
    public CarText? Summary { get; init; }

    /// <summary>Gets the title of the detail card.</summary>
    public CarText? DetailTitle { get; init; }

    /// <summary>Gets the subtitle of the detail card.</summary>
    public CarText? DetailSubtitle { get; init; }

    /// <summary>Gets the summary of the detail card.</summary>
    public CarText? DetailSummary { get; init; }

    /// <summary>Gets the map pin image; on Android Auto the marker icon of the place.</summary>
    public CarImage? PinImage { get; init; }

    /// <summary>Gets the map pin image of the selected place (CarPlay, iOS 16).</summary>
    public CarImage? SelectedPinImage { get; init; }

    /// <summary>
    /// Gets the distance to the place, shown before <see cref="Subtitle"/> on Android Auto. The Android Auto place list with a
    /// map (apps with a point-of-interest category) requires it for every place; CarPlay does not show it (put it in
    /// <see cref="Subtitle"/> there).
    /// </summary>
    public CarDistance? Distance { get; init; }

    /// <summary>
    /// Gets the primary action of the detail card (CarPlay). On Android Auto, which has no detail card, selecting the place
    /// runs this action when <see cref="OnSelected"/> is not set.
    /// </summary>
    public CarAction? PrimaryAction { get; init; }

    /// <summary>Gets the secondary action of the detail card (CarPlay only).</summary>
    public CarAction? SecondaryAction { get; init; }

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>A state of a <see cref="CarVoiceControlTemplate"/> (listening, thinking, speaking).</summary>
public sealed record CarVoiceControlState
{
    /// <summary>Creates the state.</summary>
    /// <param name="identifier">The identifier used to activate the state.</param>
    /// <param name="title">The title.</param>
    public CarVoiceControlState(string identifier, CarText title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Identifier = identifier;
        Title = title;
    }

    /// <summary>Gets the identifier.</summary>
    public string Identifier { get; }

    /// <summary>Gets the title.</summary>
    public CarText Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the image, shown when <see cref="AnimationImages"/> is empty or cannot be shown.</summary>
    public CarImage? Image { get; init; }

    /// <summary>
    /// Gets the animation frames (CarPlay). CarKit animates them only when every frame is available at once (file,
    /// resource, symbol or bytes); otherwise it shows <see cref="Image"/>. Android Auto shows <see cref="Image"/> or the
    /// first frame.
    /// </summary>
    public CarArray<CarImage> AnimationImages { get; init; }

    /// <summary>Gets how long one pass of <see cref="AnimationImages"/> takes; 1 second by default.</summary>
    public TimeSpan AnimationDuration
    {
        get;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Animation duration must be positive.");
            }

            field = value;
        }
    } = TimeSpan.FromSeconds(1);

    /// <summary>Gets a value indicating whether the animation repeats.</summary>
    public bool Repeats { get; init; }

    /// <summary>
    /// Gets the action buttons shown with the state (CarPlay iOS 26.4, at most 2; <see cref="CarFeature.VoiceControlActionButtons"/>).
    /// Selecting one runs its handler and keeps the voice control template presented. Android Auto shows the actions of the
    /// active state in its message fallback, where selecting one closes the screen.
    /// </summary>
    public CarArray<CarAction> Actions { get; init; }
}

/// <summary>Kind of a <see cref="CarNowPlayingButton"/>.</summary>
public enum CarNowPlayingButtonKind
{
    /// <summary>Shuffle.</summary>
    Shuffle,

    /// <summary>Repeat.</summary>
    Repeat,

    /// <summary>Playback rate.</summary>
    PlaybackRate,

    /// <summary>Add to library.</summary>
    AddToLibrary,

    /// <summary>More options.</summary>
    More,

    /// <summary>A custom image button.</summary>
    Custom,
}

/// <summary>A button on the now-playing screen.</summary>
public sealed record CarNowPlayingButton : CarElement
{
    /// <summary>Creates the button.</summary>
    /// <param name="kind">The button kind.</param>
    public CarNowPlayingButton(CarNowPlayingButtonKind kind) => Kind = kind;

    /// <summary>Gets the button kind.</summary>
    public CarNowPlayingButtonKind Kind { get; }

    /// <summary>Gets the image of a custom button.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets a value indicating whether the button is selected.</summary>
    public bool IsSelected { get; init; }

    /// <summary>Gets a value indicating whether the button can be selected.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}

/// <summary>Kind of a <see cref="CarPlayContactButton"/>.</summary>
public enum CarPlayContactButtonKind
{
    /// <summary>Call the contact.</summary>
    Call,

    /// <summary>Message the contact through Siri.</summary>
    Message,

    /// <summary>Directions to the contact.</summary>
    Directions,

    /// <summary>A custom button.</summary>
    Custom,
}

/// <summary>A button on a <see cref="CarPlayContactTemplate"/>.</summary>
public sealed record CarPlayContactButton : CarElement
{
    /// <summary>Creates the button.</summary>
    /// <param name="kind">The button kind.</param>
    public CarPlayContactButton(CarPlayContactButtonKind kind) => Kind = kind;

    /// <summary>Gets the button kind.</summary>
    public CarPlayContactButtonKind Kind { get; }

    /// <summary>Gets the title of a custom button.</summary>
    public CarText? Title { get; init; }

    /// <summary>Gets the image of a custom button.</summary>
    public CarImage? Image { get; init; }

    /// <summary>Gets the phone number or e-mail used by <see cref="CarPlayContactButtonKind.Message"/>.</summary>
    public string? PhoneOrEmail { get; init; }

    /// <summary>Gets the selection handler.</summary>
    public Func<CarItemContext, Task>? OnSelected { get; init; }
}
