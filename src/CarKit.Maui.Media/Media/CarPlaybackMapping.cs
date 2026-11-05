namespace CarKit.Maui.Media;

// Media3 player state (Player.STATE_*), without Android types
internal enum CarMediaPlayerState
{
    Idle,
    Buffering,
    Ready,
    Ended,
}

// Player commands exposed to Media3 controllers (Android maps them to Player.COMMAND_*)
[Flags]
internal enum CarMediaCommands
{
    None = 0,
    PlayPause = 1,
    Stop = 2,
    SeekInItem = 4,
    SkipNext = 8,
    SkipPrevious = 16,
    SeekToItem = 32,
    Shuffle = 64,
    Repeat = 128,
    Speed = 256,
    SetItems = 512,
}

// Snapshot for CarKitPlayer.GetState(): playlist, index, state, position and commands
internal sealed record CarMediaSnapshot(
    CarMediaPlayerState PlayerState,
    bool PlayWhenReady,
    long PositionMs,
    long? DurationMs,
    float Speed,
    CarRepeatMode Repeat,
    bool Shuffle,
    CarMediaCommands Commands,
    int CurrentIndex,
    IReadOnlyList<CarMediaItem> Playlist,
    string? Error);

// MPNowPlayingInfoCenter data (iOS): rate 0 outside playback, position "as of now"
internal sealed record CarNowPlayingInfo(
    string Title,
    string? Artist,
    double? DurationSeconds,
    double ElapsedSeconds,
    double Rate,
    double DefaultRate,
    int? QueueIndex,
    int? QueueCount,
    CarImage? Artwork);

internal static class CarPlaybackMapping
{
    // Playlist: the queue with a valid index, otherwise the current item, otherwise empty (index -1)
    public static IReadOnlyList<CarMediaItem> Playlist(CarPlaybackState state, out int currentIndex)
    {
        if (state.Queue.Count > 0)
        {
            var index = state.QueueIndex >= 0 && state.QueueIndex < state.Queue.Count
                ? state.QueueIndex
                : state.Item is { } item ? IndexOf(state.Queue, item.Id) : -1;
            if (index >= 0)
            {
                currentIndex = index;
                return state.Queue;
            }
        }

        if (state.Item is { } current)
        {
            currentIndex = 0;
            return [current];
        }

        currentIndex = -1;
        return [];
    }

    public static CarMediaSnapshot ToSnapshot(CarPlaybackState state, TimeSpan position, int rateCount)
    {
        var playlist = Playlist(state, out var index);
        var hasItem = playlist.Count > 0;
        var (playerState, playWhenReady) = !hasItem
            ? (CarMediaPlayerState.Idle, false)
            : state.Status switch
            {
                CarPlaybackStatus.Buffering => (CarMediaPlayerState.Buffering, true),
                CarPlaybackStatus.Playing => (CarMediaPlayerState.Ready, true),
                CarPlaybackStatus.Paused => (CarMediaPlayerState.Ready, false),
                CarPlaybackStatus.Ended => (CarMediaPlayerState.Ended, false),
                _ => (CarMediaPlayerState.Idle, false),
            };

        var commands = CarMediaCommands.PlayPause | CarMediaCommands.Stop | CarMediaCommands.SetItems | CarMediaCommands.Shuffle | CarMediaCommands.Repeat;
        if (rateCount > 1)
        {
            commands |= CarMediaCommands.Speed;
        }

        var duration = state.EffectiveDuration;
        if (hasItem)
        {
            commands |= (state.CanSeek && duration is not null ? CarMediaCommands.SeekInItem : 0)
                | (state.CanSkipNext ? CarMediaCommands.SkipNext : 0)
                | (state.CanSkipPrevious ? CarMediaCommands.SkipPrevious : 0)
                | (playlist.Count > 1 ? CarMediaCommands.SeekToItem : 0);
        }

        return new CarMediaSnapshot(
            playerState,
            playWhenReady,
            (long)Math.Max(0, position.TotalMilliseconds),
            duration is { } d ? (long)d.TotalMilliseconds : null,
            state.Rate,
            state.RepeatMode,
            state.IsShuffleEnabled,
            commands,
            index,
            playlist,
            state.Status == CarPlaybackStatus.Error ? state.ErrorMessage ?? "Playback failed." : null);
    }

    // null = nothing is playing (MPNowPlayingInfoCenter.NowPlaying = null)
    public static CarNowPlayingInfo? ToNowPlayingInfo(CarPlaybackState state, TimeSpan position)
    {
        if (state.Item is not { } item)
        {
            return null;
        }

        var hasQueue = state.Queue.Count > 0 && state.QueueIndex >= 0 && state.QueueIndex < state.Queue.Count;
        return new CarNowPlayingInfo(
            item.Title,
            item.Subtitle,
            state.EffectiveDuration?.TotalSeconds,
            Math.Max(0, position.TotalSeconds),
            state.Status == CarPlaybackStatus.Playing ? state.Rate : 0,
            state.Rate,
            hasQueue ? state.QueueIndex : null,
            hasQueue ? state.Queue.Count : null,
            item.Artwork);
    }

    private static int IndexOf(CarArray<CarMediaItem> queue, string id)
    {
        for (var i = 0; i < queue.Count; i++)
        {
            if (queue[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
