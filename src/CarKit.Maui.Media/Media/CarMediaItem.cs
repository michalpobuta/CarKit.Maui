namespace CarKit.Maui.Media;

/// <summary>How the car lays out the children of a browsable item.</summary>
public enum CarMediaChildrenStyle
{
    /// <summary>A list.</summary>
    List,

    /// <summary>A grid (Android Auto; CarPlay shows lists).</summary>
    Grid,
}

/// <summary>An item of the media library: a folder the driver can browse, a track the driver can play, or both.</summary>
public sealed record CarMediaItem
{
    /// <summary>Creates the item.</summary>
    /// <param name="id">The stable identifier passed back to <see cref="ICarMediaLibrary"/> and <see cref="ICarMediaPlayer"/>.</param>
    /// <param name="title">The title.</param>
    public CarMediaItem(string id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>Gets the stable identifier.</summary>
    public string Id
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    }

    /// <summary>Gets the title.</summary>
    public string Title { get; init => field = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>Gets the subtitle, for example the artist.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Gets the artwork.</summary>
    public CarImage? Artwork { get; init; }

    /// <summary>Gets a value indicating whether the item can be played.</summary>
    public bool IsPlayable { get; init; }

    /// <summary>Gets a value indicating whether the item has children.</summary>
    public bool IsBrowsable { get; init; }

    /// <summary>Gets how the car lays out the children of this item (browsable and playable alike).</summary>
    public CarMediaChildrenStyle ChildrenStyle { get; init; }

    /// <summary>Gets the duration of a playable item.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Gets a value indicating whether the content is explicit.</summary>
    public bool IsExplicit { get; init; }

    /// <summary>Gets how much of the item was played, from 0 to 1.</summary>
    public double? Progress
    {
        get;
        init => field = value is null or (>= 0 and <= 1) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Progress must be between 0 and 1.");
    }

    /// <summary>Gets a value indicating whether the item is playing now (CarPlay list indicator).</summary>
    public bool IsPlaying { get; init; }

    /// <summary>
    /// Gets the sports event the CarPlay now-playing screen shows while the item plays (iOS 18.4 sports mode), unless the
    /// template sets <see cref="CarNowPlayingTemplate.Sports"/> itself. Android Auto ignores it.
    /// </summary>
    public CarNowPlayingSports? Sports { get; init; }
}
