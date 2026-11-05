namespace CarKit.Maui;

/// <summary>
/// A platform feature that needs a newer car platform, a newer CarKit build or car support. Ask
/// <see cref="ICarCapabilities.Supports(CarFeature)"/> before relying on it; CarKit sends the template without an
/// unsupported feature.
/// </summary>
public enum CarFeature
{
    /// <summary>Action buttons of voice control states (CarPlay iOS 26.4; Android Auto: actions of the message fallback).</summary>
    VoiceControlActionButtons,

    /// <summary>The details header of a list (CarPlay iOS 26.4).</summary>
    ListDetailsHeader,

    /// <summary>Playback state of list items and the details header (CarPlay iOS 26.4).</summary>
    PlaybackConfiguration,

    /// <summary>The sports mode of the now-playing screen (CarPlay iOS 18.4).</summary>
    NowPlayingSportsMode,

    /// <summary>Sharing the route and its waypoints with the car's built-in navigation (CarPlay iOS 26.4, car dependent).</summary>
    RouteSharing,

    /// <summary>Video playback over AirPlay while parked (CarPlay iOS 26.4 in a car that reports video support).</summary>
    VideoPlayback,

    /// <summary>Hiding the MiniPlayer of the now-playing screen (CarPlay iOS 27; not in current CarKit builds).</summary>
    NowPlayingMiniPlayerOptOut,

    /// <summary>Showing voice control over another template (CarPlay iOS 27; not in current CarKit builds).</summary>
    VoiceControlOverlay,
}

/// <summary>How a playable item is presented.</summary>
public enum CarPlaybackPresentation
{
    /// <summary>Not specified.</summary>
    None,

    /// <summary>Audio.</summary>
    Audio,

    /// <summary>Video, played over AirPlay while parked.</summary>
    Video,
}

/// <summary>The playback action shown on a playable item.</summary>
public enum CarPlaybackAction
{
    /// <summary>No action.</summary>
    None,

    /// <summary>Play.</summary>
    Play,

    /// <summary>Pause.</summary>
    Pause,

    /// <summary>Play again from the start.</summary>
    Replay,
}

/// <summary>
/// The playback state of a list item or the details header (CarPlay iOS 26.4 <c>CPPlaybackConfiguration</c>). Update the
/// template when playback changes.
/// </summary>
public sealed record CarPlaybackConfiguration
{
    /// <summary>Gets how the item is presented.</summary>
    public CarPlaybackPresentation Presentation { get; init; }

    /// <summary>Gets the playback action shown on the item.</summary>
    public CarPlaybackAction Action { get; init; }

    /// <summary>Gets the elapsed time.</summary>
    public TimeSpan Elapsed { get; init => field = NonNegative(value, nameof(Elapsed)); }

    /// <summary>Gets the duration; <see langword="null"/> when unknown.</summary>
    public TimeSpan? Duration { get; init => field = value is { } duration ? NonNegative(duration, nameof(Duration)) : null; }

    private static TimeSpan NonNegative(TimeSpan value, string name) =>
        value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name, value, "Playback times cannot be negative.");
}

/// <summary>
/// One prominent item above the sections of a list, with a thumbnail, texts, playback state and buttons (CarPlay iOS 26.4
/// <c>CPListTemplateDetailsHeader</c>; Android Auto shows the list without it).
/// </summary>
public sealed record CarListDetailsHeader
{
    /// <summary>Creates the header.</summary>
    /// <param name="thumbnail">The thumbnail, which CarPlay requires.</param>
    public CarListDetailsHeader(CarImage thumbnail) => Thumbnail = thumbnail;

    /// <summary>Gets the thumbnail.</summary>
    public CarImage Thumbnail { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the title.</summary>
    public CarText? Title { get; init; }

    /// <summary>Gets the subtitle.</summary>
    public CarText? Subtitle { get; init; }

    /// <summary>Gets the body text; its variants go from the longest to the shortest.</summary>
    public CarText? Body { get; init; }

    /// <summary>Gets the buttons; CarPlay shows at most <c>CPListTemplateDetailsHeader.MaximumActionButtonCount</c>.</summary>
    public CarArray<CarAction> Actions { get; init; }

    /// <summary>Gets the playback state of the item the header shows.</summary>
    public CarPlaybackConfiguration? Playback { get; init; }

    /// <summary>Gets a value indicating whether CarPlay adapts the header background to the thumbnail.</summary>
    public bool UsesAdaptiveBackground { get; init; }
}

/// <summary>A team of a sports event on the now-playing screen.</summary>
public sealed record CarSportsTeam
{
    /// <summary>Creates the team.</summary>
    /// <param name="name">The team name.</param>
    public CarSportsTeam(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the team name.</summary>
    public string Name { get; }

    /// <summary>Gets the team logo; without it CarPlay shows <see cref="Initials"/>.</summary>
    public CarImage? Logo { get; init; }

    /// <summary>Gets the initials shown without a logo; by default the first three letters or digits of the name.</summary>
    public string Initials
    {
        get => field ?? DefaultInitials(Name);
        init => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Gets the standings, such as "2nd".</summary>
    public string? Standings { get; init; }

    /// <summary>Gets the score.</summary>
    public string? Score { get; init; }

    /// <summary>Gets the image shown when the team has possession.</summary>
    public CarImage? PossessionIndicator { get; init; }

    /// <summary>Gets a value indicating whether the driver follows the team.</summary>
    public bool IsFavorite { get; init; }

    private static string DefaultInitials(string name) =>
        new string([.. name.Where(char.IsLetterOrDigit).Take(3)]).ToUpperInvariant();
}

/// <summary>The clock of a sports event.</summary>
public sealed record CarSportsClock
{
    /// <summary>Creates the clock.</summary>
    /// <param name="time">The elapsed time when <paramref name="countsUp"/> is <see langword="true"/>, otherwise the time remaining.</param>
    /// <param name="countsUp">Whether the clock counts up from the start of the period.</param>
    public CarSportsClock(TimeSpan time, bool countsUp)
    {
        if (time < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(time), time, "The clock time cannot be negative.");
        }

        Time = time;
        CountsUp = countsUp;
    }

    /// <summary>Gets the clock time.</summary>
    public TimeSpan Time { get; }

    /// <summary>Gets a value indicating whether the clock counts up.</summary>
    public bool CountsUp { get; }

    /// <summary>Gets a value indicating whether the clock is stopped.</summary>
    public bool IsPaused { get; init; }
}

/// <summary>
/// The sports mode of the now-playing screen: two teams, the event status and clock (CarPlay iOS 18.4
/// <c>CPNowPlayingModeSports</c>; Android Auto shows the regular now-playing screen).
/// </summary>
public sealed record CarNowPlayingSports
{
    /// <summary>Creates the sports mode.</summary>
    /// <param name="leftTeam">The team on the left.</param>
    /// <param name="rightTeam">The team on the right.</param>
    public CarNowPlayingSports(CarSportsTeam leftTeam, CarSportsTeam rightTeam)
    {
        LeftTeam = leftTeam;
        RightTeam = rightTeam;
    }

    /// <summary>Gets the team on the left.</summary>
    public CarSportsTeam LeftTeam { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the team on the right.</summary>
    public CarSportsTeam RightTeam { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the event status, such as "2nd half".</summary>
    public CarText? Status { get; init; }

    /// <summary>Gets the event status image.</summary>
    public CarImage? StatusImage { get; init; }

    /// <summary>Gets the event clock.</summary>
    public CarSportsClock? Clock { get; init; }

    /// <summary>Gets the background artwork.</summary>
    public CarImage? BackgroundArtwork { get; init; }
}

// Features rendered by the latest CarKit asset (net10.0-ios26.4, Car App 1.7.0.1); FakeCarHost simulates it by default.
// iOS 27 features will arrive with the net10.0-ios27.0 asset (Xcode 27; spec: "New platform features decisions (Stage
// 9)")
internal static class CarFeatureDefaults
{
    public static IReadOnlySet<CarFeature> NewestAsset { get; } = new HashSet<CarFeature>
    {
        CarFeature.VoiceControlActionButtons,
        CarFeature.ListDetailsHeader,
        CarFeature.PlaybackConfiguration,
        CarFeature.NowPlayingSportsMode,
        CarFeature.RouteSharing,
        CarFeature.VideoPlayback,
    };
}
