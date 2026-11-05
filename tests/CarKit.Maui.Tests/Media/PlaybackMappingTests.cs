using CarKit.Maui.Media;
using Xunit;

namespace CarKit.Maui.Tests.Media;

// Player state → Media3 (SimpleBasePlayer.State) and MPNowPlayingInfo, without platform types
public class PlaybackMappingTests
{
    private static readonly CarMediaItem _a = TestMediaLibrary.Track("a", "A");
    private static readonly CarMediaItem _b = TestMediaLibrary.Track("b", "B");

    [Fact]
    public void Snapshot_maps_status_commands_and_queue()
    {
        var paused = CarPlaybackMapping.ToSnapshot(
            new CarPlaybackState { Status = CarPlaybackStatus.Paused, Item = _b, Queue = [_a, _b], QueueIndex = 1, RepeatMode = CarRepeatMode.All, IsShuffleEnabled = true },
            TimeSpan.FromSeconds(12), rateCount: 2);
        Assert.Equal(CarMediaPlayerState.Ready, paused.PlayerState);
        Assert.False(paused.PlayWhenReady);
        Assert.Equal(12_000, paused.PositionMs);
        Assert.Equal(180_000, paused.DurationMs);
        Assert.Equal(1, paused.CurrentIndex);
        Assert.Equal(["a", "b"], paused.Playlist.Select(i => i.Id));
        Assert.True(paused.Shuffle);
        Assert.Equal(CarRepeatMode.All, paused.Repeat);
        Assert.Equal(
            CarMediaCommands.PlayPause | CarMediaCommands.Stop | CarMediaCommands.SetItems | CarMediaCommands.Shuffle | CarMediaCommands.Repeat
            | CarMediaCommands.Speed | CarMediaCommands.SeekInItem | CarMediaCommands.SkipNext | CarMediaCommands.SkipPrevious | CarMediaCommands.SeekToItem,
            paused.Commands);

        var idle = CarPlaybackMapping.ToSnapshot(new CarPlaybackState { Status = CarPlaybackStatus.Playing }, TimeSpan.Zero, rateCount: 1);
        Assert.Equal(CarMediaPlayerState.Idle, idle.PlayerState);
        Assert.False(idle.PlayWhenReady);
        Assert.Empty(idle.Playlist);
        Assert.Equal(-1, idle.CurrentIndex);
        Assert.Equal(CarMediaCommands.None, idle.Commands & (CarMediaCommands.SkipNext | CarMediaCommands.Speed | CarMediaCommands.SeekInItem));

        var limited = CarPlaybackMapping.ToSnapshot(
            new CarPlaybackState { Status = CarPlaybackStatus.Playing, Item = _a with { Duration = null }, CanSkipNext = false, CanSkipPrevious = false },
            TimeSpan.Zero, rateCount: 1);
        Assert.Equal(CarMediaPlayerState.Ready, limited.PlayerState);
        Assert.True(limited.PlayWhenReady);
        Assert.Null(limited.DurationMs);
        Assert.Equal(CarMediaCommands.None, limited.Commands & (CarMediaCommands.SkipNext | CarMediaCommands.SkipPrevious | CarMediaCommands.SeekInItem | CarMediaCommands.SeekToItem));
    }

    [Theory]
    [InlineData(CarPlaybackStatus.Buffering, "Buffering", true)]
    [InlineData(CarPlaybackStatus.Stopped, "Idle", false)]
    [InlineData(CarPlaybackStatus.Ended, "Ended", false)]
    [InlineData(CarPlaybackStatus.Idle, "Idle", false)]
    [InlineData(CarPlaybackStatus.Error, "Idle", false)]
    public void Statuses_map_to_media3_states(CarPlaybackStatus status, string expectedState, bool playWhenReady)
    {
        var expected = Enum.Parse<CarMediaPlayerState>(expectedState);
        var snapshot = CarPlaybackMapping.ToSnapshot(new CarPlaybackState { Status = status, Item = _a }, TimeSpan.Zero, 1);

        Assert.Equal(expected, snapshot.PlayerState);
        Assert.Equal(playWhenReady, snapshot.PlayWhenReady);
        Assert.Equal(status == CarPlaybackStatus.Error ? "Playback failed." : null, snapshot.Error);
    }

    [Fact]
    public void Queue_without_a_valid_index_uses_the_current_item()
    {
        var byId = CarPlaybackMapping.Playlist(new CarPlaybackState { Item = _b, Queue = [_a, _b] }, out var index);
        Assert.Equal(2, byId.Count);
        Assert.Equal(1, index);

        var outside = CarPlaybackMapping.Playlist(new CarPlaybackState { Item = _b, Queue = [_a], QueueIndex = 5 }, out var single);
        Assert.Equal(["b"], outside.Select(i => i.Id));
        Assert.Equal(0, single);
    }

    [Fact]
    public void Now_playing_info_has_zero_rate_when_paused()
    {
        var playing = CarPlaybackMapping.ToNowPlayingInfo(
            new CarPlaybackState { Status = CarPlaybackStatus.Playing, Item = _a, Rate = 1.5f, Queue = [_a, _b], QueueIndex = 0 }, TimeSpan.FromSeconds(30))!;
        Assert.Equal("A", playing.Title);
        Assert.Equal("Artist", playing.Artist);
        Assert.Equal(180, playing.DurationSeconds);
        Assert.Equal(30, playing.ElapsedSeconds);
        Assert.Equal(1.5, playing.Rate, 3);
        Assert.Equal(1.5, playing.DefaultRate, 3);
        Assert.Equal(0, playing.QueueIndex);
        Assert.Equal(2, playing.QueueCount);

        var paused = CarPlaybackMapping.ToNowPlayingInfo(new CarPlaybackState { Status = CarPlaybackStatus.Paused, Item = _a }, TimeSpan.FromSeconds(30))!;
        Assert.Equal(0, paused.Rate);
        Assert.Null(paused.QueueIndex);
        Assert.Null(CarPlaybackMapping.ToNowPlayingInfo(new CarPlaybackState(), TimeSpan.Zero));
    }

    [Fact]
    public void Model_rejects_invalid_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarPlaybackState { Rate = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _a with { Progress = 1.5 });
        Assert.Throws<ArgumentException>(() => new CarMediaItem("", "x"));
        Assert.Throws<ArgumentNullException>(() => new CarMediaItem("x", null!));
    }
}
