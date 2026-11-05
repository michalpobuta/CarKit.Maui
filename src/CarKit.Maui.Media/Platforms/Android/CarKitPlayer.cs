using Android.Content;
using Android.OS;
using AndroidX.Media3.Common;
using Google.Common.Util.Concurrent;
using Microsoft.Extensions.Logging;
using MediaPlayerState = AndroidX.Media3.Common.SimpleBasePlayer.State;
using P = AndroidX.Media3.Common.BasePlayer.InterfaceConsts;

namespace CarKit.Maui.Media;

// ICarMediaPlayer as a Media3 player (spec: "Media module decisions (Stage 5)"): SimpleBasePlayer on the main loop,
// GetState() from the controller snapshot (CarPlaybackMapping), Media3 controller commands (Android Auto, notification)
// → controller. A command's Future completes once it is handled, and every player state change → InvalidateState().
internal sealed class CarKitPlayer : SimpleBasePlayer
{
    private static readonly int[] _alwaysAvailable =
    [
        P.CommandPrepare, P.CommandGetCurrentMediaItem, P.CommandGetTimeline, P.CommandGetMetadata, P.CommandGetAudioAttributes,
        P.CommandSeekToDefaultPosition, P.CommandRelease,
    ];

    private readonly CarMediaRuntime _runtime;
    private readonly Context _context;
    private bool _released;

    public CarKitPlayer(CarMediaRuntime runtime, Context context)
        : base(Looper.MainLooper!)
    {
        _runtime = runtime;
        _context = context;
        runtime.Controller.StateChanged += OnStateChanged;
    }

    private CarMediaController Controller => _runtime.Controller;

    protected override MediaPlayerState GetState()
    {
        var snapshot = CarPlaybackMapping.ToSnapshot(Controller.State, Controller.Position, _runtime.Options.PlaybackRates.Count);
        var builder = new MediaPlayerState.Builder()
            .SetAvailableCommands(Commands(snapshot.Commands))!
            .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(C.UsageMedia)!.SetContentType(C.AudioContentTypeMusic)!.Build()!)!
            .SetPlayWhenReady(snapshot.PlayWhenReady, P.PlayWhenReadyChangeReasonUserRequest)!
            .SetPlaybackState(snapshot.PlayerState switch
            {
                CarMediaPlayerState.Buffering => P.StateBuffering,
                CarMediaPlayerState.Ready => P.StateReady,
                CarMediaPlayerState.Ended => P.StateEnded,
                _ => P.StateIdle,
            })!
            .SetRepeatMode(snapshot.Repeat switch
            {
                CarRepeatMode.One => P.RepeatModeOne,
                CarRepeatMode.All => P.RepeatModeAll,
                _ => P.RepeatModeOff,
            })!
            .SetShuffleModeEnabled(snapshot.Shuffle)!
            .SetPlaybackParameters(new PlaybackParameters(snapshot.Speed))!;
        if (snapshot.Playlist.Count > 0)
        {
            builder.SetPlaylist(Playlist(snapshot))!
                .SetCurrentMediaItemIndex(snapshot.CurrentIndex)!
                .SetContentPositionMs(snapshot.PositionMs);
        }

        if (snapshot.Error is { } error)
        {
            builder.SetPlayerError(new PlaybackException(error, null, PlaybackException.ErrorCodeUnspecified));
        }

        return builder.Build()!;
    }

    protected override IListenableFuture HandleSetPlayWhenReady(bool playWhenReady) => CarMediaFutures.From(Controller.SetPlayWhenReadyAsync(playWhenReady));

    protected override IListenableFuture HandlePrepare() => CarMediaFutures.Done(null);

    protected override IListenableFuture HandleStop() => CarMediaFutures.From(Controller.PauseAsync());

    // setMediaItems from browsing: the start item plays (at once or on the next play); the start position is skipped
    // (the app player starts the track itself, resuming a position is its decision)
    protected override IListenableFuture HandleSetMediaItems(IList<MediaItem>? mediaItems, int startIndex, long startPositionMs)
    {
        if (mediaItems is not { Count: > 0 })
        {
            return CarMediaFutures.Done(null);
        }

        var index = startIndex >= 0 && startIndex < mediaItems.Count ? startIndex : 0;
        var id = mediaItems[index].MediaId;
        return string.IsNullOrEmpty(id) ? CarMediaFutures.Done(null) : CarMediaFutures.From(Controller.SetItemAsync(id));
    }

    protected override IListenableFuture HandleSeek(int mediaItemIndex, long positionMs, int seekCommand)
    {
        var kind = seekCommand switch
        {
            P.CommandSeekToNext or P.CommandSeekToNextMediaItem => CarMediaSeekKind.Next,
            P.CommandSeekToPrevious or P.CommandSeekToPreviousMediaItem => CarMediaSeekKind.Previous,
            P.CommandSeekToMediaItem => CarMediaSeekKind.ToItem,
            _ => CarMediaSeekKind.InItem,
        };
        var position = positionMs == C.TimeUnset ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Max(0, positionMs));
        return CarMediaFutures.From(Controller.SeekToAsync(kind, mediaItemIndex, position));
    }

    protected override IListenableFuture HandleSetShuffleModeEnabled(bool shuffleModeEnabled) => CarMediaFutures.From(Controller.SetShuffleAsync(shuffleModeEnabled));

    protected override IListenableFuture HandleSetRepeatMode(int repeatMode) => CarMediaFutures.From(Controller.SetRepeatAsync(repeatMode switch
    {
        P.RepeatModeOne => CarRepeatMode.One,
        P.RepeatModeAll => CarRepeatMode.All,
        _ => CarRepeatMode.Off,
    }));

    protected override IListenableFuture HandleSetPlaybackParameters(PlaybackParameters? playbackParameters) =>
        playbackParameters is { Speed: > 0 } parameters ? CarMediaFutures.From(Controller.SetRateAsync(parameters.Speed)) : CarMediaFutures.Done(null);

    // The queue is driven by the app player: adding, moving, removing and replacing items (COMMAND_CHANGE_MEDIA_ITEMS,
    // e.g. addQueueItem from an AVRCP radio or clearMediaItems) change nothing; without an implementation
    // SimpleBasePlayer throws IllegalStateException on the main loop (review I1)
    protected override IListenableFuture HandleAddMediaItems(int index, IList<MediaItem>? mediaItems) => CarMediaFutures.Done(null);

    protected override IListenableFuture HandleMoveMediaItems(int fromIndex, int toIndex, int newIndex) => CarMediaFutures.Done(null);

    protected override IListenableFuture HandleRemoveMediaItems(int fromIndex, int toIndex) => CarMediaFutures.Done(null);

    protected override IListenableFuture HandleReplaceMediaItems(int fromIndex, int toIndex, IList<MediaItem>? mediaItems) => CarMediaFutures.Done(null);

    protected override IListenableFuture HandleRelease()
    {
        Detach();
        return CarMediaFutures.Done(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Detach();
        }

        base.Dispose(disposing);
    }

    private static PlayerCommands Commands(CarMediaCommands commands)
    {
        var builder = new PlayerCommands.Builder().AddAll(_alwaysAvailable)!;
        Add(builder, commands, CarMediaCommands.PlayPause, P.CommandPlayPause);
        Add(builder, commands, CarMediaCommands.Stop, P.CommandStop);
        Add(builder, commands, CarMediaCommands.SetItems, P.CommandSetMediaItem, P.CommandChangeMediaItems);
        Add(builder, commands, CarMediaCommands.SeekInItem, P.CommandSeekInCurrentMediaItem, P.CommandSeekBack, P.CommandSeekForward);
        Add(builder, commands, CarMediaCommands.SkipNext, P.CommandSeekToNext, P.CommandSeekToNextMediaItem);
        Add(builder, commands, CarMediaCommands.SkipPrevious, P.CommandSeekToPrevious, P.CommandSeekToPreviousMediaItem);
        Add(builder, commands, CarMediaCommands.SeekToItem, P.CommandSeekToMediaItem);
        Add(builder, commands, CarMediaCommands.Shuffle, P.CommandSetShuffleMode);
        Add(builder, commands, CarMediaCommands.Repeat, P.CommandSetRepeatMode);
        Add(builder, commands, CarMediaCommands.Speed, P.CommandSetSpeedAndPitch);
        return builder.Build()!;
    }

    private static void Add(PlayerCommands.Builder builder, CarMediaCommands commands, CarMediaCommands flag, params int[] media3)
    {
        if ((commands & flag) != 0)
        {
            builder.AddAll(media3);
        }
    }

    // A unique uid per position (the same track can be in the queue twice); the current item's time from the state
    private List<MediaItemData> Playlist(CarMediaSnapshot snapshot)
    {
        var playlist = new List<MediaItemData>(snapshot.Playlist.Count);
        for (var i = 0; i < snapshot.Playlist.Count; i++)
        {
            var item = snapshot.Playlist[i];
            var durationMs = i == snapshot.CurrentIndex ? snapshot.DurationMs : (long?)item.Duration?.TotalMilliseconds;
            playlist.Add(new MediaItemData.Builder(new Java.Lang.String(i + ":" + item.Id))
                .SetMediaItem(CarMediaItems.ToMediaItem(item, _context, _runtime.Logger))!
                .SetDurationUs(durationMs is { } ms ? ms * 1000 : C.TimeUnset)!
                .SetIsSeekable((snapshot.Commands & CarMediaCommands.SeekInItem) != 0 && i == snapshot.CurrentIndex)!
                .Build()!);
        }

        return playlist;
    }

    private void OnStateChanged(CarPlaybackState state)
    {
        if (_released)
        {
            return;
        }

        try
        {
            InvalidateState();
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalStateException or Java.Lang.IllegalArgumentException)
        {
            // State inconsistent with the SimpleBasePlayer rules (e.g. a wrong queue index): an app model error, not a
            // failure
            CarMediaLog.Warning(_runtime.Logger, "Media3 rejected the player state", ex);
            _runtime.Errors.HandleError(ex, new CarErrorContext("Media.State", null));
        }
    }

    private void Detach()
    {
        if (!_released)
        {
            _released = true;
            _runtime.Controller.StateChanged -= OnStateChanged;
        }
    }
}
