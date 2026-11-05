using CarKit.Maui.Renderers;
using CoreFoundation;
using Foundation;
using MediaPlayer;
using UIKit;

namespace CarKit.Maui.Media;

// Now Playing bridge (spec: "Media module decisions (Stage 5)"): player state → MPNowPlayingInfoCenter,
// MPRemoteCommandCenter commands (lock screen, CarPlay, headphones) → controller. The command handlers call Handle*
// methods, which device tests invoke directly (an MPRemoteCommandEvent cannot be created). Main thread only.
internal sealed class CarPlayNowPlayingBridge(CarMediaRuntime runtime) : IDisposable
{
    // Artwork up to 600 px (the lock screen and CarPlay show it at that size at most)
    private const int ArtworkPixels = 600;

    private readonly List<(MPRemoteCommand Command, NSObject Target)> _targets = [];
    private CarImageLoader<UIImage>? _images;
    private (CarImage Source, MPMediaItemArtwork Artwork)? _artwork;
    private bool _started;

    private CarMediaController Controller => runtime.Controller;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _images = new CarImageLoader<UIImage>(new CarPlayImages(UIScreen.MainScreen.Scale), runtime.Logger, combineDarkVariants: false);
        var center = MPRemoteCommandCenter.Shared;
        Add(center.PlayCommand, _ => HandlePlay());
        Add(center.PauseCommand, _ => HandlePause());
        Add(center.TogglePlayPauseCommand, _ => HandleToggle());
        Add(center.StopCommand, _ => HandlePause());
        Add(center.NextTrackCommand, _ => HandleNext());
        Add(center.PreviousTrackCommand, _ => HandlePrevious());
        Add(center.ChangePlaybackPositionCommand, e => e is MPChangePlaybackPositionCommandEvent position ? HandlePosition(position.PositionTime) : MPRemoteCommandHandlerStatus.CommandFailed);
        Add(center.ChangeShuffleModeCommand, e => e is MPChangeShuffleModeCommandEvent shuffle ? HandleShuffle(shuffle.ShuffleType) : MPRemoteCommandHandlerStatus.CommandFailed);
        Add(center.ChangeRepeatModeCommand, e => e is MPChangeRepeatModeCommandEvent repeat ? HandleRepeat(repeat.RepeatType) : MPRemoteCommandHandlerStatus.CommandFailed);
        Add(center.ChangePlaybackRateCommand, e => e is MPChangePlaybackRateCommandEvent rate ? HandleRate(rate.PlaybackRate) : MPRemoteCommandHandlerStatus.CommandFailed);
        center.ChangePlaybackRateCommand.SupportedPlaybackRates = [.. runtime.Options.PlaybackRates.Select(r => NSNumber.FromFloat(r))];
        Controller.StateChanged += Publish;
        Publish(Controller.State);
    }

    internal MPRemoteCommandHandlerStatus HandlePlay() => Run(Controller.ResumeAsync);

    internal MPRemoteCommandHandlerStatus HandlePause() => Run(Controller.PauseAsync);

    internal MPRemoteCommandHandlerStatus HandleToggle() => Run(Controller.TogglePlayPauseAsync);

    internal MPRemoteCommandHandlerStatus HandleNext() => Run(Controller.SkipNextAsync);

    internal MPRemoteCommandHandlerStatus HandlePrevious() => Run(Controller.SkipPreviousAsync);

    internal MPRemoteCommandHandlerStatus HandlePosition(double seconds) => Run(() => Controller.SeekAsync(TimeSpan.FromSeconds(seconds)));

    internal MPRemoteCommandHandlerStatus HandleShuffle(MPShuffleType type) => Run(() => Controller.SetShuffleAsync(type != MPShuffleType.Off));

    internal MPRemoteCommandHandlerStatus HandleRepeat(MPRepeatType type) => Run(() => Controller.SetRepeatAsync(type switch
    {
        MPRepeatType.One => CarRepeatMode.One,
        MPRepeatType.All => CarRepeatMode.All,
        _ => CarRepeatMode.Off,
    }));

    internal MPRemoteCommandHandlerStatus HandleRate(float rate) => Run(() => Controller.SetRateAsync(rate));

    public void Dispose()
    {
        Controller.StateChanged -= Publish;
        foreach (var (command, target) in _targets)
        {
            command.RemoveTarget(target);
        }

        _targets.Clear();
        _images?.Dispose();
        _started = false;
    }

    // Also on position/rate: the system extrapolates from ElapsedPlaybackTime and PlaybackRate
    private void Publish(CarPlaybackState _)
    {
        var state = Controller.State;
        var center = MPRemoteCommandCenter.Shared;
        var hasItem = state.Item is not null;
        center.PlayCommand.Enabled = hasItem;
        center.PauseCommand.Enabled = hasItem;
        center.TogglePlayPauseCommand.Enabled = hasItem;
        center.StopCommand.Enabled = hasItem;
        center.NextTrackCommand.Enabled = hasItem && state.CanSkipNext;
        center.PreviousTrackCommand.Enabled = hasItem && state.CanSkipPrevious;
        center.ChangePlaybackPositionCommand.Enabled = hasItem && state.CanSeek && state.EffectiveDuration is not null;
        center.ChangeShuffleModeCommand.Enabled = hasItem;
        center.ChangeShuffleModeCommand.CurrentShuffleType = state.IsShuffleEnabled ? MPShuffleType.Items : MPShuffleType.Off;
        center.ChangeRepeatModeCommand.Enabled = hasItem;
        center.ChangeRepeatModeCommand.CurrentRepeatType = state.RepeatMode switch
        {
            CarRepeatMode.One => MPRepeatType.One,
            CarRepeatMode.All => MPRepeatType.All,
            _ => MPRepeatType.Off,
        };
        center.ChangePlaybackRateCommand.Enabled = hasItem && runtime.Options.PlaybackRates.Count > 1;

        if (CarPlaybackMapping.ToNowPlayingInfo(state, Controller.Position) is not { } info)
        {
            MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = null!;
            return;
        }

        MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo
        {
            Title = info.Title,
            Artist = info.Artist,
            PlaybackDuration = info.DurationSeconds,
            ElapsedPlaybackTime = info.ElapsedSeconds,
            PlaybackRate = info.Rate,
            DefaultPlaybackRate = info.DefaultRate,
            PlaybackQueueIndex = info.QueueIndex,
            PlaybackQueueCount = info.QueueCount,
            Artwork = Artwork(info.Artwork)!,
        };
    }

    // Artwork from the CarKit image pipeline; an image from a URI/stream → published again after loading, if still the
    // same
    private MPMediaItemArtwork? Artwork(CarImage? source)
    {
        if (source is null || _images is null)
        {
            return null;
        }

        if (_artwork is { } cached && cached.Source.Equals(source))
        {
            return cached.Artwork;
        }

        var load = _images.Load(source, new CarImageRequest(ArtworkPixels, CarContentStyle.Light));
        if (load.Image is { } image)
        {
            var artwork = new MPMediaItemArtwork(image.Size, _ => image);
            _artwork = (source, artwork);
            return artwork;
        }

        load.Pending?.ContinueWith(
            task =>
            {
                if (task.Result is not null)
                {
                    DispatchQueue.MainQueue.DispatchAsync(() =>
                    {
                        if (Controller.State.Item?.Artwork?.Equals(source) == true)
                        {
                            Publish(Controller.State);
                        }
                    });
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        return null;
    }

    private void Add(MPRemoteCommand command, Func<MPRemoteCommandEvent, MPRemoteCommandHandlerStatus> handler) =>
        _targets.Add((command, command.AddTarget(handler)));

    // A command without an item makes no sense; the player result (exception) goes to ICarErrorHandler through the
    // controller
    private MPRemoteCommandHandlerStatus Run(Func<Task> command)
    {
        if (Controller.State.Item is null)
        {
            return MPRemoteCommandHandlerStatus.NoActionableNowPlayingItem;
        }

        command().Forget(runtime.Errors, "Media.RemoteCommand");
        return MPRemoteCommandHandlerStatus.Success;
    }
}
