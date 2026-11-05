namespace CarKit.Maui.Media;

/// <summary>What the player is doing.</summary>
public enum CarPlaybackStatus
{
    /// <summary>Nothing is loaded.</summary>
    Idle,

    /// <summary>Loading before playing.</summary>
    Buffering,

    /// <summary>Playing.</summary>
    Playing,

    /// <summary>Paused.</summary>
    Paused,

    /// <summary>Stopped, with the item still loaded.</summary>
    Stopped,

    /// <summary>Reached the end.</summary>
    Ended,

    /// <summary>Failed; see <see cref="CarPlaybackState.ErrorMessage"/>.</summary>
    Error,
}

/// <summary>How playback repeats.</summary>
public enum CarRepeatMode
{
    /// <summary>No repeat.</summary>
    Off,

    /// <summary>Repeat the current item.</summary>
    One,

    /// <summary>Repeat the queue.</summary>
    All,
}

/// <summary>A snapshot of the app player, emitted by <see cref="ICarMediaPlayer.State"/>.</summary>
public sealed record CarPlaybackState
{
    /// <summary>Gets the status.</summary>
    public CarPlaybackStatus Status { get; init; }

    /// <summary>Gets the current item.</summary>
    public CarMediaItem? Item { get; init; }

    /// <summary>Gets the position in the current item when the state was emitted; CarKit advances it while playing.</summary>
    public TimeSpan Position { get; init; }

    /// <summary>Gets the duration of the current item; <see langword="null"/> uses <see cref="CarMediaItem.Duration"/>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Gets the playback rate, where 1 is normal speed (the rate the player plays at, also while paused).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The rate is zero, negative or not finite.</exception>
    public float Rate
    {
        get;
        init => field = value > 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The rate must be positive.");
    } = 1f;

    /// <summary>Gets a value indicating whether shuffle is on.</summary>
    public bool IsShuffleEnabled { get; init; }

    /// <summary>Gets the repeat mode.</summary>
    public CarRepeatMode RepeatMode { get; init; }

    /// <summary>Gets the queue (playlist) in playing order.</summary>
    public CarArray<CarMediaItem> Queue { get; init; }

    /// <summary>Gets the index of the current item in <see cref="Queue"/>, or -1 without a queue.</summary>
    public int QueueIndex { get; init; } = -1;

    /// <summary>Gets a value indicating whether the player can skip to the next item.</summary>
    public bool CanSkipNext { get; init; } = true;

    /// <summary>Gets a value indicating whether the player can skip to the previous item.</summary>
    public bool CanSkipPrevious { get; init; } = true;

    /// <summary>Gets a value indicating whether the player can seek in the current item.</summary>
    public bool CanSeek { get; init; } = true;

    /// <summary>Gets the error shown to the driver when <see cref="Status"/> is <see cref="CarPlaybackStatus.Error"/>.</summary>
    public string? ErrorMessage { get; init; }

    // Playback is in progress or about to start (interruption policy, audio focus)
    internal bool IsActive => Status is CarPlaybackStatus.Playing or CarPlaybackStatus.Buffering;

    internal TimeSpan? EffectiveDuration => Duration ?? Item?.Duration;
}
