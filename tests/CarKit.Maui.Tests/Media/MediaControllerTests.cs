using CarKit.Maui.Media;
using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Tests.Media;

public class MediaControllerTests
{
    // Review Focus 1: state from any thread reaches the controller and the events on the main thread
    [Fact]
    public async Task State_changes_from_any_thread_arrive_on_the_main_thread()
    {
        using var dispatcher = new FakeCarDispatcher();
        var player = new TestMediaPlayer();
        var errors = new RecordingErrors();
        using var controller = await dispatcher.DispatchAsync(() =>
            Task.FromResult(new CarMediaController(player, TestMediaFactory.Options(), errors, dispatcher, TimeProvider.System)));
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.StateChanged += state => arrived.TrySetResult(!dispatcher.IsDispatchRequired && state.Status == CarPlaybackStatus.Playing);

        await Task.Run(() => player.Emit(TestMediaFactory.Playing()), TestContext.Current.CancellationToken);

        Assert.True(await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(CarPlaybackStatus.Playing, controller.State.Status);
        Assert.Empty(errors.Errors);
    }

    // Review Focus 2: position counted from the moment the state is received (Media3 extrapolates from it further)
    [Fact]
    public void Position_advances_from_the_time_the_state_arrived()
    {
        var (controller, player, time, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing(seconds: 10) with { Rate = 1.5f });

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(13), controller.Position);

        player.Emit(controller.State with { Status = CarPlaybackStatus.Paused, Position = TimeSpan.FromSeconds(10) });
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), controller.Position);

        // Not beyond the end of the track (3 min)
        player.Emit(TestMediaFactory.Playing(seconds: 179));
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMinutes(3), controller.Position);
    }

    [Fact]
    public async Task Player_errors_go_to_the_error_handler()
    {
        var (controller, player, _, errors) = TestMediaFactory.Controller();
        player.FailWith = new InvalidOperationException("boom");

        await controller.PauseAsync();
        await controller.PlayAsync("t2");

        Assert.Equal(["Media.Pause", "Media.Play"], errors.Errors.Select(e => e.Context.Operation));
    }

    [Fact]
    public async Task Toggle_shuffle_repeat_and_rate_cycle()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller(1f, 1.5f, 2f);

        await controller.ToggleShuffleAsync();
        player.Emit(new CarPlaybackState { IsShuffleEnabled = true, RepeatMode = CarRepeatMode.All, Rate = 2f });
        await controller.ToggleShuffleAsync();
        await controller.CycleRepeatAsync();
        player.Emit(new CarPlaybackState { RepeatMode = CarRepeatMode.One, Rate = 1f });
        await controller.CycleRepeatAsync();
        player.Emit(new CarPlaybackState { RepeatMode = CarRepeatMode.Off, Rate = 2f });
        await controller.CycleRepeatAsync();
        await controller.CycleRateAsync();
        player.Emit(new CarPlaybackState { Rate = 1.5f });
        await controller.CycleRateAsync();
        player.Emit(new CarPlaybackState { Rate = 0.8f });
        await controller.CycleRateAsync();

        Assert.Equal(
            ["Shuffle:True", "Shuffle:False", "Repeat:One", "Repeat:Off", "Repeat:All", "Rate:1", "Rate:2", "Rate:1"],
            player.Calls);
    }

    [Fact]
    public async Task Buttons_without_handlers_run_default_actions()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller(1f, 2f);

        await controller.PressAsync(new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle));
        await controller.PressAsync(new CarNowPlayingButton(CarNowPlayingButtonKind.Repeat));
        await controller.PressAsync(new CarNowPlayingButton(CarNowPlayingButtonKind.PlaybackRate));
        await controller.PressAsync(new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "like" });
        await controller.PressAsync(new CarNowPlayingButton(CarNowPlayingButtonKind.AddToLibrary) { Id = "add" });

        Assert.Equal(["Shuffle:True", "Repeat:All", "Rate:2", "Custom:like", "Custom:add"], player.Calls);
    }

    // Review Focus 4: Android Auto plays from browsing (setMediaItems + play) with a single PlayAsync
    [Fact]
    public async Task Set_media_items_then_play_starts_the_item_once()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing("t1") with { Status = CarPlaybackStatus.Paused });

        await controller.SetItemAsync("t2");
        Assert.Empty(player.Calls);
        await controller.SetPlayWhenReadyAsync(true);
        Assert.Equal(["Play:t2"], player.Calls);

        player.Emit(TestMediaFactory.Playing("t2"));
        await controller.SetItemAsync("t3");
        await controller.SetPlayWhenReadyAsync(true);
        Assert.Equal(["Play:t2", "Play:t3"], player.Calls);

        // Without an item in the queue: a plain resume and pause
        player.Emit(TestMediaFactory.Playing("t3") with { Status = CarPlaybackStatus.Paused });
        await controller.SetPlayWhenReadyAsync(true);
        await controller.SetPlayWhenReadyAsync(false);
        Assert.Equal(["Play:t2", "Play:t3", "Resume", "Pause"], player.Calls);
    }

    [Fact]
    public async Task Seek_kinds_route_to_skip_seek_or_queue_item()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing("a") with { Queue = [TestMediaLibrary.Track("a", "A"), TestMediaLibrary.Track("b", "B")], QueueIndex = 0 });

        await controller.SeekToAsync(CarMediaSeekKind.Next, 1, TimeSpan.Zero);
        await controller.SeekToAsync(CarMediaSeekKind.Previous, 0, TimeSpan.Zero);
        await controller.SeekToAsync(CarMediaSeekKind.InItem, 0, TimeSpan.FromSeconds(5));
        await controller.SeekToAsync(CarMediaSeekKind.ToItem, 1, TimeSpan.Zero);
        await controller.SeekToAsync(CarMediaSeekKind.ToItem, 0, TimeSpan.FromSeconds(7));
        await controller.SeekAsync(TimeSpan.FromSeconds(-3));

        Assert.Equal(["SkipNext", "SkipPrevious", "Seek:5", "Play:b", "Seek:7", "Seek:0"], player.Calls);
    }

    // Review I6: state from a worker thread (queued) does not overwrite a newer state that already arrived on the main thread
    [Fact]
    public void Older_state_from_a_worker_does_not_overwrite_a_newer_one()
    {
        var dispatcher = new ManualDispatcher();
        var player = new TestMediaPlayer();
        using var controller = new CarMediaController(player, TestMediaFactory.Options(), new RecordingErrors(), dispatcher, TimeProvider.System);
        var seen = new List<CarPlaybackStatus>();
        controller.StateChanged += state => seen.Add(state.Status);

        dispatcher.OnMainThread = false;
        player.Emit(TestMediaFactory.Playing());
        dispatcher.OnMainThread = true;
        player.Emit(TestMediaFactory.Playing() with { Status = CarPlaybackStatus.Paused });
        dispatcher.Pump();

        Assert.Equal(CarPlaybackStatus.Paused, controller.State.Status);
        Assert.Equal([CarPlaybackStatus.Paused], seen);
    }
}
