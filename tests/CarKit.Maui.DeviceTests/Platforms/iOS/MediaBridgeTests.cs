using AVFoundation;
using CarKit.Maui.Media;
using CarKit.Maui.Renderers;
using CarPlay;
using Foundation;
using MediaPlayer;
using Microsoft.Extensions.DependencyInjection;
using UIKit;

namespace CarKit.Maui.DeviceTests;

// Media module on the iOS simulator (Stage 5): the MPNowPlayingInfoCenter/MPRemoteCommandCenter and AVAudioSession bridge
// from the test app's UseMedia, interruptions via posted AVAudioSession notifications, auto-browse and Now Playing on CP*
internal static class MediaBridgeTests
{
    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Audio_session_uses_the_playback_category), Audio_session_uses_the_playback_category),
        new(nameof(Now_playing_info_follows_the_player_state), Now_playing_info_follows_the_player_state),
        new(nameof(Remote_commands_route_to_the_player), Remote_commands_route_to_the_player),
        new(nameof(Interruption_pauses_and_resume_option_resumes), Interruption_pauses_and_resume_option_resumes),
        new(nameof(Old_device_unavailable_pauses), Old_device_unavailable_pauses),
        new(nameof(Auto_browse_renders_tabs_of_lists), Auto_browse_renders_tabs_of_lists),
        new(nameof(Now_playing_template_configures_the_shared_template), Now_playing_template_configures_the_shared_template),
        new(nameof(Now_playing_observer_runs_the_handlers), Now_playing_observer_runs_the_handlers),
    ];

    private static IServiceProvider Services => MediaTestServices.Services;

    private static DeviceTestMediaPlayer Player => MediaTestServices.Player;

    private static CarPlaybackState Queue(CarPlaybackState state) =>
        state with { Queue = [DeviceTestMediaLibrary.Track(1), DeviceTestMediaLibrary.Track(2)], QueueIndex = 0 };

    private static CarPlayRenderContext Context(TestSession test) => new(
        test.Session, test, new CarImageLoader<UIImage>(new CarPlayImages(2), test, combineDarkVariants: true), 2, () => CarContentStyle.Light,
        _ => Task.CompletedTask, [.. Services.GetServices<ICarPlayModuleRenderer>()]);

    private static Task Audio_session_uses_the_playback_category()
    {
        // The NSString value of the category (AVAudioSessionCategoryPlayback)
        Check.Equal("AVAudioSessionCategoryPlayback", AVAudioSession.SharedInstance().Category.ToString());
        return Task.CompletedTask;
    }

    private static async Task Now_playing_info_follows_the_player_state()
    {
        Player.Reset(Queue(MediaTestServices.Playing(1, 30) with { Rate = 1.5f }));
        await Check.Eventually(() => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying?.Title == "Track 1", "now playing title");
        var info = MPNowPlayingInfoCenter.DefaultCenter.NowPlaying!;
        Check.Equal("Artist", info.Artist);
        Check.Equal(180d, info.PlaybackDuration);
        Check.True(info.ElapsedPlaybackTime is >= 30 and < 32);
        Check.Equal(1.5, Math.Round(info.PlaybackRate ?? 0, 3));
        Check.Equal(1.5, Math.Round(info.DefaultPlaybackRate ?? 0, 3));
        Check.Equal(0, info.PlaybackQueueIndex);
        Check.Equal(2, info.PlaybackQueueCount);

        Player.Emit(Queue(MediaTestServices.Playing(1, 40)) with { Status = CarPlaybackStatus.Paused });
        await Check.Eventually(() => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying?.PlaybackRate == 0, "paused rate 0");
        Check.Equal(40d, MPNowPlayingInfoCenter.DefaultCenter.NowPlaying!.ElapsedPlaybackTime);

        Player.Reset();
        await Check.Eventually(() => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying?.Title is null, "now playing cleared");
    }

    private static async Task Remote_commands_route_to_the_player()
    {
        var bridge = Services.GetRequiredService<CarPlayNowPlayingBridge>();
        var center = MPRemoteCommandCenter.Shared;
        Player.Reset(Queue(MediaTestServices.Playing(1)) with { IsShuffleEnabled = true, RepeatMode = CarRepeatMode.All, CanSkipNext = false });

        Check.True(!center.NextTrackCommand.Enabled);
        Check.True(center.PreviousTrackCommand.Enabled);
        Check.True(center.ChangePlaybackPositionCommand.Enabled);
        Check.Equal(MPShuffleType.Items, center.ChangeShuffleModeCommand.CurrentShuffleType);
        Check.Equal(MPRepeatType.All, center.ChangeRepeatModeCommand.CurrentRepeatType);
        Check.Equal("1,1.5,2", string.Join(",", center.ChangePlaybackRateCommand.SupportedPlaybackRates.Select(r => r.FloatValue.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandlePause());
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandlePlay());
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandleToggle());
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandleNext());
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandlePrevious());
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandlePosition(42.4));
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandleShuffle(MPShuffleType.Off));
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandleRepeat(MPRepeatType.One));
        Check.Equal(MPRemoteCommandHandlerStatus.Success, bridge.HandleRate(2f));
        await MediaTestServices.WaitForCall("Rate:2");
        Check.Equal("Pause,Resume,Pause,SkipNext,SkipPrevious,Seek:42,Shuffle:False,Repeat:One,Rate:2", string.Join(",", Player.Calls));

        Player.Reset();
        Check.Equal(MPRemoteCommandHandlerStatus.NoActionableNowPlayingItem, bridge.HandlePlay());
        Check.True(!center.PlayCommand.Enabled);
    }

    private static void PostInterruption(AVAudioSessionInterruptionType type, bool shouldResume)
    {
        var info = new NSMutableDictionary
        {
            [new NSString("AVAudioSessionInterruptionTypeKey")] = NSNumber.FromNUInt((nuint)(ulong)type),
        };
        if (type == AVAudioSessionInterruptionType.Ended)
        {
            info[new NSString("AVAudioSessionInterruptionOptionKey")] = NSNumber.FromNUInt(shouldResume ? (nuint)(ulong)AVAudioSessionInterruptionOptions.ShouldResume : 0);
        }

        NSNotificationCenter.DefaultCenter.PostNotificationName(AVAudioSession.InterruptionNotification, AVAudioSession.SharedInstance(), info);
    }

    // Stage 5 acceptance: "interrupted by a call and playback returns": the notifications iOS sends during a call
    private static async Task Interruption_pauses_and_resume_option_resumes()
    {
        Player.Reset(MediaTestServices.Playing(1));

        PostInterruption(AVAudioSessionInterruptionType.Began, shouldResume: false);
        await MediaTestServices.WaitForCall("Pause");
        Player.Emit(MediaTestServices.Playing(1) with { Status = CarPlaybackStatus.Paused });
        PostInterruption(AVAudioSessionInterruptionType.Ended, shouldResume: true);
        await MediaTestServices.WaitForCall("Resume");

        // Without ShouldResume (e.g. another app started playing) playback stays stopped
        Player.Reset(MediaTestServices.Playing(1));
        PostInterruption(AVAudioSessionInterruptionType.Began, shouldResume: false);
        await MediaTestServices.WaitForCall("Pause");
        Player.Emit(MediaTestServices.Playing(1) with { Status = CarPlaybackStatus.Paused });
        PostInterruption(AVAudioSessionInterruptionType.Ended, shouldResume: false);
        await Task.Delay(300);
        Check.True(!Player.Calls.Contains("Resume"));
        Player.Reset();
    }

    private static async Task Old_device_unavailable_pauses()
    {
        Player.Reset(MediaTestServices.Playing(1));
        var info = new NSMutableDictionary
        {
            [new NSString("AVAudioSessionRouteChangeReasonKey")] = NSNumber.FromNUInt((nuint)(ulong)AVAudioSessionRouteChangeReason.OldDeviceUnavailable),
        };

        NSNotificationCenter.DefaultCenter.PostNotificationName(AVAudioSession.RouteChangeNotification, AVAudioSession.SharedInstance(), info);

        await MediaTestServices.WaitForCall("Pause");
        Player.Reset();
    }

    // Auto-browse (the handler from UseMedia) on a CarPlay session in Audio, then the real renderers: CPTabBarTemplate with
    // CPListTemplate
    private static async Task Auto_browse_renders_tabs_of_lists()
    {
        Player.Reset(MediaTestServices.Playing(1));
        var test = TestSession.CarPlay(CarAppCategory.Audio);
        await test.Session.StartAsync(Services.GetRequiredService<ICarAppHandler>());

        Check.True(test.Errors.Count == 0, "session errors: " + string.Join(" | ", test.Errors.Select(e => e.Message)) + " app errors: " + DeviceTestErrors.Instance);
        var tabs = Check.Is<CarTabTemplate>(test.Templates.Single());
        var native = Check.Is<CPTabBarTemplate>(CarPlayTemplateRenderers.Render(tabs, Context(test)).Native);
        Check.Equal(3, native.Templates.Length);
        Check.Equal("Albums,Tracks,Playlists", string.Join(",", native.Templates.Select(t => t.TabTitle)));
        var tracks = Check.Is<CPListTemplate>(native.Templates[1]);
        var rows = tracks.Sections[0].AllItems<CPListItem>().ToArray();
        Check.Equal(5, rows.Length);
        Check.Equal("Track 1", rows[0].Text);
        Check.True(rows[0].IsPlaying);
        Check.True(rows[1].IsExplicitContent);
        Check.Equal(0.5f, (float)rows[2].PlaybackProgress);
        Player.Reset();
    }

    private static async Task Now_playing_template_configures_the_shared_template()
    {
        Player.Reset(MediaTestServices.Playing(1));
        var test = TestSession.CarPlay(CarAppCategory.Audio);
        // A connected session: the renderer updates the buttons only for the current connection
        await test.Session.StartAsync(new IdleHandler());
        var model = new CarNowPlayingTemplate
        {
            Buttons =
            [
                new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle),
                new CarNowPlayingButton(CarNowPlayingButtonKind.Repeat),
                new CarNowPlayingButton(CarNowPlayingButtonKind.PlaybackRate),
                new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "like", Image = CarImage.FromSystemSymbol("heart", null), IsSelected = true },
            ],
            IsUpNextButtonEnabled = true,
            UpNextTitle = "Next",
            IsAlbumArtistButtonEnabled = true,
        }.ConfigureCarPlay(np =>
        {
            // Sports mode (iOS 18.4) through the escape hatch; CarKit does not overwrite NowPlayingMode
            if (OperatingSystem.IsIOSVersionAtLeast(18, 4))
            {
                np.NowPlayingMode = new CPNowPlayingModeSports(
                    new CPNowPlayingSportsTeam("Home", new CPNowPlayingSportsTeamLogo("HOM"), "1.", "2", null!, true),
                    new CPNowPlayingSportsTeam("Away", new CPNowPlayingSportsTeamLogo("AWY"), "3.", "1", null!, false),
                    new CPNowPlayingSportsEventStatus(["2nd half"], null!, new CPNowPlayingSportsClock(3000, false, CPNowPlayingSportsClock.CPNowPlayingSportsClockTimeOption.ElapsedTime)),
                    null!);
            }
        });
        var context = Context(test);

        var state = CarPlayTemplateRenderers.Render(model, context);

        var native = Check.Is<CPNowPlayingTemplate>(state.Native);
        Check.True(ReferenceEquals(CPNowPlayingTemplate.SharedTemplate, native));
        Check.True(native.IsUpNextButtonEnabled);
        Check.Equal("Next", native.UpNextTitle);
        Check.True(native.IsAlbumArtistButtonEnabled);
        var buttons = native.NowPlayingButtons;
        Check.Is<CPNowPlayingShuffleButton>(buttons[0]);
        Check.Is<CPNowPlayingRepeatButton>(buttons[1]);
        Check.Is<CPNowPlayingPlaybackRateButton>(buttons[2]);
        Check.Is<CPNowPlayingImageButton>(buttons[3]);
        Check.True(buttons[3].Selected);
        if (OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            Check.Is<CPNowPlayingModeSports>(native.NowPlayingMode);
        }

        // Shuffle/Repeat: CarPlay ignores Selected on these buttons (probe: the setter does not stick) and draws the state
        // from MPRemoteCommandCenter; the bridge sets CurrentShuffleType/CurrentRepeatType from the player state
        var probe = new CPNowPlayingShuffleButton(_ => { }) { Selected = true };
        Check.True(!probe.Selected);
        Player.Emit(MediaTestServices.Playing(1) with { IsShuffleEnabled = true, RepeatMode = CarRepeatMode.One });
        Check.Equal(MPShuffleType.Items, MPRemoteCommandCenter.Shared.ChangeShuffleModeCommand.CurrentShuffleType);
        Check.Equal(MPRepeatType.One, MPRemoteCommandCenter.Shared.ChangeRepeatModeCommand.CurrentRepeatType);
        Check.True(DeviceTestErrors.Instance.Errors.Count == 0, "app errors: " + DeviceTestErrors.Instance);

        // The update is always in place; the hook runs again
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, model with { IsUpNextButtonEnabled = false }, context));
        Check.True(!native.IsUpNextButtonEnabled);
        Player.Reset();
    }

    private static async Task Now_playing_observer_runs_the_handlers()
    {
        var test = TestSession.CarPlay(CarAppCategory.Audio);
        var upNext = 0;
        var albumArtist = 0;
        var model = new CarNowPlayingTemplate
        {
            IsUpNextButtonEnabled = true,
            OnUpNextSelected = _ =>
            {
                upNext++;
                return Task.CompletedTask;
            },
            OnAlbumArtistSelected = _ =>
            {
                albumArtist++;
                return Task.CompletedTask;
            },
        };
        await test.Session.StartAsync(new NowPlayingHandler(model));
        CarPlayTemplateRenderers.Render(model, Context(test));
        // Stage 8: next to the Now Playing renderer there is the Navigation module's map renderer
        var renderer = Services.GetServices<ICarPlayModuleRenderer>().OfType<NowPlayingTemplateRenderer>().Single();

        renderer.OnUpNext();
        renderer.OnAlbumArtist();

        await Check.Eventually(() => upNext == 1 && albumArtist == 1, "Up Next and Album/Artist handlers");
        Check.Equal(0, test.Errors.Count);
    }

    private sealed class IdleHandler : ICarAppHandler
    {
        public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NowPlayingHandler(CarNowPlayingTemplate model) : ICarAppHandler
    {
        public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => session.SetRootAsync(new CarListTemplate("Root")).ContinueWith(_ => session.PushAsync(model), ct).Unwrap();
    }
}
