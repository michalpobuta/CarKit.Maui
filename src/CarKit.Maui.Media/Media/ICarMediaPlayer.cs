namespace CarKit.Maui.Media;

/// <summary>
/// Adapter over the app's own player. CarKit routes the car's playback commands (CarPlay remote commands and Now
/// Playing buttons, Android Auto through Media3) to it and shows its <see cref="State"/> in the car.
/// </summary>
/// <remarks>
/// <see cref="State"/> must emit the current state when an observer subscribes and every change after that; it may
/// emit on any thread. CarKit also pauses and resumes the player for interruptions such as phone calls: on Android it
/// requests audio focus for the player, so the player must not request focus itself (or set
/// <see cref="CarMediaBuilder.HandleAudioFocus"/> to <see langword="false"/>).
/// </remarks>
public interface ICarMediaPlayer
{
    /// <summary>Gets the playback state stream.</summary>
    IObservable<CarPlaybackState> State { get; }

    /// <summary>Starts playing an item.</summary>
    /// <param name="itemId">The <see cref="CarMediaItem.Id"/>.</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task PlayAsync(string itemId);

    /// <summary>Pauses playback.</summary>
    /// <returns>A task that completes when the command is handled.</returns>
    Task PauseAsync();

    /// <summary>Resumes playback of the current item.</summary>
    /// <returns>A task that completes when the command is handled.</returns>
    Task ResumeAsync();

    /// <summary>Moves to a position in the current item.</summary>
    /// <param name="position">The position from the start of the item.</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SeekAsync(TimeSpan position);

    /// <summary>Skips to the next item.</summary>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SkipNextAsync();

    /// <summary>Skips to the previous item (or the start of the current one).</summary>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SkipPreviousAsync();

    /// <summary>Turns shuffle on or off.</summary>
    /// <param name="on">Whether shuffle is on.</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SetShuffleAsync(bool on);

    /// <summary>Sets the repeat mode.</summary>
    /// <param name="mode">The repeat mode.</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SetRepeatAsync(CarRepeatMode mode);

    /// <summary>Sets the playback rate.</summary>
    /// <param name="rate">The rate, where 1 is normal speed.</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task SetRateAsync(float rate);

    /// <summary>Runs an app-defined action, such as a custom Now Playing button.</summary>
    /// <param name="actionId">The action identifier (the <see cref="CarElement.Id"/> of the button).</param>
    /// <returns>A task that completes when the command is handled.</returns>
    Task InvokeCustomActionAsync(string actionId);
}
