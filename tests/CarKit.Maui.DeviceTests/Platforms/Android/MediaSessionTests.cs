using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using AndroidX.Car.App.Media.Model;
using AndroidX.Car.App.Model;
using AndroidX.Core.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using CarKit.Maui.Media;
using CarKit.Maui.Renderers;
using Google.Common.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static AndroidX.Media3.Session.MediaLibraryService;
using P = AndroidX.Media3.Common.BasePlayer.InterfaceConsts;

namespace CarKit.Maui.DeviceTests;

// Media module on the emulator (Stage 5): a MediaBrowser connects to the generated CarKitGeneratedMediaLibraryService the
// way Android Auto does (browsing, search, commands), and a competing AudioFocusRequest simulates a phone call
internal static class MediaSessionTests
{
    private const string ServiceName = "carkit.maui.generated.CarKitGeneratedMediaLibraryService";

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Browser_reads_root_and_children_with_paging), Browser_reads_root_and_children_with_paging),
        new(nameof(Browser_gets_items_and_search_results), Browser_gets_items_and_search_results),
        new(nameof(Player_state_reaches_the_controller), Player_state_reaches_the_controller),
        new(nameof(Play_from_browse_and_transport_commands_reach_the_player), Play_from_browse_and_transport_commands_reach_the_player),
        new(nameof(Session_buttons_follow_the_state_and_run_commands), Session_buttons_follow_the_state_and_run_commands),
        new(nameof(Legacy_controller_sees_the_state_metadata_and_custom_actions), Legacy_controller_sees_the_state_metadata_and_custom_actions),
        new(nameof(Buttons_without_android_icons_are_skipped), Buttons_without_android_icons_are_skipped),
        new(nameof(Queue_change_commands_leave_the_app_queue), Queue_change_commands_leave_the_app_queue),
        new(nameof(Failed_search_still_reports_a_result_count), Failed_search_still_reports_a_result_count),
        new(nameof(Voice_play_from_search_plays_the_first_match), Voice_play_from_search_plays_the_first_match),
        new(nameof(Audio_focus_loss_pauses_and_gain_resumes), Audio_focus_loss_pauses_and_gain_resumes),
        new(nameof(Media_playback_template_renders_with_hooks), Media_playback_template_renders_with_hooks),
    ];

    private static Context App => Android.App.Application.Context;

    private static async Task<T> Await<T>(IListenableFuture? future)
        where T : Java.Lang.Object
    {
        var done = new TaskCompletionSource<Java.Lang.Object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listenable = Check.NotNull(future);
        listenable.AddListener(new Java.Lang.Runnable(() =>
        {
            try
            {
                done.TrySetResult(listenable.Get());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }), ContextCompat.GetMainExecutor(App));
        var value = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return Check.NotNull(value).JavaCast<T>()!;
    }

    // A connection like Android Auto's: the service's SessionToken from the manifest (MediaLibraryService intent filter) →
    // MediaBrowser
    private static async Task<MediaBrowser> ConnectAsync(MediaBrowser.IListener? listener = null)
    {
        var token = new SessionToken(App, new ComponentName(App.PackageName!, ServiceName));
        var builder = new MediaBrowser.Builder(App, token);
        if (listener is not null)
        {
            builder.SetListener(listener);
        }

        return await Await<MediaBrowser>(builder.BuildAsync());
    }

    private static List<MediaItem> Items(LibraryResult result)
    {
        Check.Equal(LibraryResult.ResultSuccess, result.ResultCode);
        var list = Check.NotNull(result.Value).JavaCast<Java.Util.IList>()!;
        return [.. Enumerable.Range(0, list.Size()).Select(i => list.Get(i)!.JavaCast<MediaItem>()!)];
    }

    private static async Task Browser_reads_root_and_children_with_paging()
    {
        MediaTestServices.Player.Reset();
        var browser = await ConnectAsync();
        try
        {
            var root = await Await<LibraryResult>(browser.GetLibraryRoot(null));
            var rootItem = Check.NotNull(root.Value).JavaCast<MediaItem>()!;
            Check.Equal("root", rootItem.MediaId);
            Check.True(rootItem.MediaMetadata!.IsBrowsable!.BooleanValue());
            Check.True(root.Params?.Extras?.GetBoolean(CarMediaItems.SearchSupportedKey) == true);

            var first = Items(await Await<LibraryResult>(browser.GetChildren("root", 0, 2, null)));
            var second = Items(await Await<LibraryResult>(browser.GetChildren("root", 1, 2, null)));
            Check.Equal("albums,tracks", string.Join(",", first.Select(i => i.MediaId)));
            Check.Equal("lists", string.Join(",", second.Select(i => i.MediaId)));
            Check.Equal(MediaConstants.ExtrasValueContentStyleGridItem, first[0].MediaMetadata!.Extras!.GetInt(MediaConstants.ExtrasKeyContentStyleBrowsable));
            Check.Equal(MediaConstants.ExtrasValueContentStyleListItem, first[1].MediaMetadata!.Extras!.GetInt(MediaConstants.ExtrasKeyContentStylePlayable));

            var tracks = Items(await Await<LibraryResult>(browser.GetChildren("tracks", 0, 10, null)));
            Check.Equal(5, tracks.Count);
            Check.True(tracks[0].MediaMetadata!.IsPlayable!.BooleanValue());
            Check.Equal("Artist", tracks[0].MediaMetadata!.Artist?.ToString());
            Check.Equal(MediaConstants.ExtrasValueAttributePresent, tracks[1].MediaMetadata!.Extras!.GetLong(MediaConstants.ExtrasKeyIsExplicit));
            Check.Equal(MediaConstants.ExtrasValueCompletionStatusPartiallyPlayed, tracks[2].MediaMetadata!.Extras!.GetInt(MediaConstants.ExtrasKeyCompletionStatus));

            var album = Items(await Await<LibraryResult>(browser.GetChildren("albums", 0, 10, null))).Single();
            Check.True(album.MediaMetadata!.ArtworkUri?.ToString()?.StartsWith("android.resource://", StringComparison.Ordinal) == true);
        }
        finally
        {
            browser.Release();
        }
    }

    private static async Task Browser_gets_items_and_search_results()
    {
        var listener = new SearchListener();
        var browser = await ConnectAsync(listener);
        try
        {
            var item = Check.NotNull((await Await<LibraryResult>(browser.GetItem("t4"))).Value).JavaCast<MediaItem>()!;
            Check.Equal("Track 4", item.MediaMetadata!.Title?.ToString());
            Check.Equal(SessionError.ErrorBadValue, (await Await<LibraryResult>(browser.GetItem("missing"))).ResultCode);

            Check.Equal(LibraryResult.ResultSuccess, (await Await<LibraryResult>(browser.Search("track", null))).ResultCode);
            await Check.Eventually(() => listener.Count == 5, "search result count");
            var page = Items(await Await<LibraryResult>(browser.GetSearchResult("track", 1, 2, null)));
            Check.Equal("t3,t4", string.Join(",", page.Select(i => i.MediaId)));
        }
        finally
        {
            browser.Release();
        }
    }

    // Review Focus 1: state from a thread pool thread reaches the Media3 controller (InvalidateState on the main loop)
    private static async Task Player_state_reaches_the_controller()
    {
        var player = MediaTestServices.Player;
        player.Reset();
        var browser = await ConnectAsync();
        try
        {
            await Task.Run(() => player.Emit(MediaTestServices.Playing(3, 10) with
            {
                IsShuffleEnabled = true,
                RepeatMode = CarRepeatMode.One,
                Rate = 1.5f,
                Queue = [DeviceTestMediaLibrary.Track(3), DeviceTestMediaLibrary.Track(4)],
                QueueIndex = 0,
            }));
            await Check.Eventually(() => browser.IsPlaying && browser.CurrentMediaItem?.MediaId == "t3", "controller playing t3");
            Check.True(browser.ShuffleModeEnabled);
            Check.Equal(P.RepeatModeOne, browser.RepeatMode);
            Check.Equal(1.5f, browser.PlaybackParameters!.Speed);
            Check.Equal(2, browser.MediaItemCount);
            Check.Equal(180_000L, browser.Duration);
            Check.True(browser.CurrentPosition >= 10_000);
            Check.Equal("Track 3", browser.MediaMetadata!.Title?.ToString());

            player.Emit(MediaTestServices.Playing(3, 20) with { Status = CarPlaybackStatus.Paused, CanSkipNext = false });
            await Check.Eventually(() => !browser.PlayWhenReady, "controller paused");
            Check.Equal(P.StateReady, browser.PlaybackState);
            Check.True(!browser.IsCommandAvailable(P.CommandSeekToNext));
            Check.True(browser.IsCommandAvailable(P.CommandSeekInCurrentMediaItem));

            // The position in every GetState() is computed "as of now" (Review Focus 2): a command the player does not echo
            // makes Media3 read the state again; without extrapolation the controller would get 10 s again
            player.Emit(MediaTestServices.Playing(3, 10));
            await Check.Eventually(() => browser.IsPlaying, "playing again");
            await Task.Delay(1500);
            browser.RepeatMode = P.RepeatModeAll;
            await MediaTestServices.WaitForCall("Repeat:All");
            await Task.Delay(200);
            Check.True(browser.CurrentPosition >= 11_000, $"position {browser.CurrentPosition} ms after 1.5 s of playing from 10 s");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    // Review Focus 4: setMediaItems + prepare + play from browsing → a single PlayAsync(id); then the transport commands
    private static async Task Play_from_browse_and_transport_commands_reach_the_player()
    {
        var player = MediaTestServices.Player;
        player.Reset();
        var browser = await ConnectAsync();
        try
        {
            var item = new MediaItem.Builder().SetMediaId("t2")!.Build()!;
            browser.SetMediaItems([item]);
            browser.Prepare();
            browser.Play();
            await MediaTestServices.WaitForCall("Play:t2");
            Check.True(!player.Calls.Contains("Resume"));

            player.Emit(MediaTestServices.Playing(2) with { Queue = [DeviceTestMediaLibrary.Track(2), DeviceTestMediaLibrary.Track(3)], QueueIndex = 0 });
            await Check.Eventually(() => browser.CurrentMediaItem?.MediaId == "t2" && browser.IsPlaying, "controller sees t2");

            browser.Pause();
            await MediaTestServices.WaitForCall("Pause");
            browser.SeekTo(42_000);
            await MediaTestServices.WaitForCall("Seek:42");
            browser.SeekToNext();
            await MediaTestServices.WaitForCall("SkipNext");
            browser.SeekToPrevious();
            await MediaTestServices.WaitForCall("SkipPrevious");
            browser.SeekTo(1, 0);
            await MediaTestServices.WaitForCall("Play:t3");
            browser.ShuffleModeEnabled = true;
            await MediaTestServices.WaitForCall("Shuffle:True");
            browser.RepeatMode = P.RepeatModeAll;
            await MediaTestServices.WaitForCall("Repeat:All");
            browser.SetPlaybackSpeed(2f);
            await MediaTestServices.WaitForCall("Rate:2");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    private static async Task Session_buttons_follow_the_state_and_run_commands()
    {
        var player = MediaTestServices.Player;
        player.Reset(MediaTestServices.Playing(1));
        var browser = await ConnectAsync();
        try
        {
            CommandButton? Shuffle() => Buttons(browser).FirstOrDefault(b => b.SessionCommand?.CustomAction == CarNowPlayingButtons.ShuffleCommand);
            await Check.Eventually(() => Shuffle()?.Icon == CommandButton.IconShuffleOff, "shuffle button (off)");
            Check.True(Buttons(browser).Any(b => b.SessionCommand?.CustomAction == CarNowPlayingButtons.RateCommand && b.Icon == CommandButton.IconPlaybackSpeed));

            player.Emit(MediaTestServices.Playing(1) with { IsShuffleEnabled = true, RepeatMode = CarRepeatMode.All });
            await Check.Eventually(() => Shuffle()?.Icon == CommandButton.IconShuffleOn, "shuffle button (on)");
            Check.True(Buttons(browser).Any(b => b.SessionCommand?.CustomAction == CarNowPlayingButtons.RepeatCommand && b.Icon == CommandButton.IconRepeatAll));

            var result = await Await<SessionResult>(browser.SendCustomCommand(new SessionCommand(CarNowPlayingButtons.ShuffleCommand, Bundle.Empty!), Bundle.Empty!));
            Check.Equal(SessionResult.ResultSuccess, result.ResultCode);
            await MediaTestServices.WaitForCall("Shuffle:False");
            await Await<SessionResult>(browser.SendCustomCommand(new SessionCommand(CarNowPlayingButtons.RateCommand, Bundle.Empty!), Bundle.Empty!));
            await MediaTestServices.WaitForCall("Rate:1.5");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    // Android Auto connects through MediaBrowserCompat (a platform session): the state, metadata, queue and session
    // buttons as PlaybackState custom actions must be visible there too
    private static async Task Legacy_controller_sees_the_state_metadata_and_custom_actions()
    {
        var player = MediaTestServices.Player;
        player.Reset(MediaTestServices.Playing(2) with { Queue = [DeviceTestMediaLibrary.Track(2), DeviceTestMediaLibrary.Track(3)], QueueIndex = 0 });
        var browser = await ConnectAsync();
        try
        {
            var session = Check.NotNull(CarMediaSessions.Current);
            using var legacy = new Android.Media.Session.MediaController(App, session.PlatformToken!);
            await Check.Eventually(() => legacy.PlaybackState?.State == Android.Media.Session.PlaybackStateCode.Playing, "legacy PLAYING");
            Check.Equal("Track 2", legacy.Metadata?.GetString(Android.Media.MediaMetadata.MetadataKeyTitle));
            Check.Equal(2, legacy.Queue?.Count ?? 0);
            await Check.Eventually(
                () => legacy.PlaybackState?.CustomActions?.Any(a => a.Action == CarNowPlayingButtons.ShuffleCommand) == true,
                "legacy custom actions (" + string.Join(",", legacy.PlaybackState?.CustomActions?.Select(a => a.Action) ?? []) + ")");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    // Review C1: Media3 throws IllegalArgumentException for a legacy action without an icon, so buttons without an Android
    // icon are skipped
    private static async Task Buttons_without_android_icons_are_skipped()
    {
        var player = MediaTestServices.Player;
        player.Reset(MediaTestServices.Playing(1));
        var browser = await ConnectAsync();
        try
        {
            var session = Check.NotNull(CarMediaSessions.Current);
            using var legacy = new Android.Media.Session.MediaController(App, session.PlatformToken!);
            await Check.Eventually(() => legacy.PlaybackState?.CustomActions?.Count > 0, "legacy custom actions");
            var actions = legacy.PlaybackState!.CustomActions!;
            Check.True(actions.All(a => a.Icon != 0));
            var names = string.Join(",", actions.Select(a => a.Action));
            Check.True(names.Contains("carkit.custom:add", StringComparison.Ordinal) && names.Contains("carkit.custom:like", StringComparison.Ordinal), names);
            Check.True(!names.Contains("carkit.custom:more", StringComparison.Ordinal) && !names.Contains("no-icon", StringComparison.Ordinal), names);

            await Await<SessionResult>(browser.SendCustomCommand(new SessionCommand("carkit.custom:like", Bundle.Empty!), Bundle.Empty!));
            await MediaTestServices.WaitForCall("Custom:like");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    // Review I1: adding, removing, moving and clearing the queue from the controller does not bring down SimpleBasePlayer
    private static async Task Queue_change_commands_leave_the_app_queue()
    {
        var player = MediaTestServices.Player;
        player.Reset(MediaTestServices.Playing(1) with { Queue = [DeviceTestMediaLibrary.Track(1), DeviceTestMediaLibrary.Track(2)], QueueIndex = 0 });
        var browser = await ConnectAsync();
        try
        {
            await Check.Eventually(() => browser.MediaItemCount == 2, "queue of 2");
            browser.AddMediaItem(new MediaItem.Builder().SetMediaId("t5")!.Build()!);
            browser.MoveMediaItem(0, 1);
            browser.RemoveMediaItem(1);
            browser.ClearMediaItems();
            await Task.Delay(500);
            Check.Equal(2, browser.MediaItemCount);
            Check.Equal("t1", browser.CurrentMediaItem?.MediaId);
            Check.True(DeviceTestErrors.Instance.Errors.All(e => !e.Contains("Media.State", StringComparison.Ordinal)), DeviceTestErrors.Instance.ToString());
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    private static async Task Failed_search_still_reports_a_result_count()
    {
        var listener = new SearchListener();
        var browser = await ConnectAsync(listener);
        try
        {
            Check.Equal(LibraryResult.ResultSuccess, (await Await<LibraryResult>(browser.Search("fail", null))).ResultCode);
            await Check.Eventually(() => listener.Count == 0, "search result count 0 after a failure");
            Check.Equal(SessionError.ErrorUnknown, (await Await<LibraryResult>(browser.GetSearchResult("fail", 0, 10, null))).ResultCode);
        }
        finally
        {
            browser.Release();
        }
    }

    // Review I9: "Hey Google, play Track 3": the legacy onPlayFromSearch arrives as an item without a MediaId, with the
    // query
    private static async Task Voice_play_from_search_plays_the_first_match()
    {
        var player = MediaTestServices.Player;
        player.Reset();
        var browser = await ConnectAsync();
        try
        {
            var session = Check.NotNull(CarMediaSessions.Current);
            using var legacy = new Android.Media.Session.MediaController(App, session.PlatformToken!);
            legacy.GetTransportControls().PlayFromSearch("Track 3", Bundle.Empty);
            await MediaTestServices.WaitForCall("Play:t3");
        }
        finally
        {
            browser.Release();
            player.Reset();
        }
    }

    // Stage 5 acceptance: "interrupted by a call and playback returns": the call takes the focus (GAIN_TRANSIENT) and gives
    // it back after hanging up; the module pauses and resumes the app's player
    private static async Task Audio_focus_loss_pauses_and_gain_resumes()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            throw new CheckException("The competing AudioFocusRequest needs Android 8 (API 26); the emulator runs API 35.");
        }

        var player = MediaTestServices.Player;
        var focus = MediaTestServices.Services.GetRequiredService<CarAudioFocus>();
        player.Reset(MediaTestServices.Playing(1));
        await Check.Eventually(() => focus.IsHeld, "CarKit audio focus");

        var audio = (AudioManager)App.GetSystemService(Context.AudioService)!;
        using var call = new AudioFocusRequestClass.Builder(AudioFocus.GainTransient)
            .SetAudioAttributes(new Android.Media.AudioAttributes.Builder().SetUsage(AudioUsageKind.VoiceCommunication)!.Build()!)!
            .SetOnAudioFocusChangeListener(new FocusListener())!
            .Build()!;
        Check.Equal(AudioFocusRequest.Granted, audio.RequestAudioFocus(call));
        try
        {
            await MediaTestServices.WaitForCall("Pause");
            player.Emit(MediaTestServices.Playing(1) with { Status = CarPlaybackStatus.Paused });
            Check.True(!player.Calls.Contains("Resume"));
        }
        finally
        {
            audio.AbandonAudioFocusRequest(call);
        }

        await MediaTestServices.WaitForCall("Resume");
        player.Reset();
        await Check.Eventually(() => !focus.IsHeld, "CarKit released the focus when idle");
    }

    private static Task Media_playback_template_renders_with_hooks()
    {
        var services = MediaTestServices.Services;
        var real = services.GetRequiredService<CarMediaRuntime>();
        var runtime = new CarMediaRuntime(real.Library, real.Controller, real.Options with { TemplatedMedia = true }, real.Errors, NullLogger.Instance);
        var test = TestSession.AndroidAuto(8);
        var context = new CarAppRenderContext(
            test.Session, test, 8, App, new CarImageLoader<CarIcon>(new CarAppImages(App), test, combineDarkVariants: false),
            () => CarContentStyle.Light, _ => { }, useSectionedItemTemplate: true, test, [new MediaPlaybackTemplateRenderer(runtime)]);
        var hooked = 0;
        var model = new CarNowPlayingTemplate { Title = "Now playing" }.ConfigureAndroidAuto(_ => hooked++);

        var rendered = CarAppTemplateRenderers.Render(model, context, new CarAppScreenState());

        var template = Check.Is<MediaPlaybackTemplate>(rendered.Template);
        Check.Equal("Now playing", template.Header?.Title?.ToString());
        Check.Equal(CarAppTemplateTypes.MediaPlayback, rendered.Shape.Type);
        Check.Equal(1, hooked);

        // Without the media templates flag the module's renderer does not respond (Android Auto draws playback itself)
        var plain = new CarAppRenderContext(
            test.Session, test, 8, App, new CarImageLoader<CarIcon>(new CarAppImages(App), test, combineDarkVariants: false),
            () => CarContentStyle.Light, _ => { }, useSectionedItemTemplate: true, test, [new MediaPlaybackTemplateRenderer(real)]);
        try
        {
            CarAppTemplateRenderers.Render(model, plain, new CarAppScreenState());
            throw new CheckException("Expected NotSupportedException without templated media");
        }
        catch (NotSupportedException)
        {
        }

        return Task.CompletedTask;
    }

    private static List<CommandButton> Buttons(MediaBrowser browser)
    {
        var list = browser.MediaButtonPreferences;
        return list is null ? [] : [.. Enumerable.Range(0, list.Size()).Select(i => ((Java.Lang.Object)list.Get(i)!).JavaCast<CommandButton>()!)];
    }

    // All listener methods explicitly: default methods of the Java interface from the binding end in AbstractMethodError
    private sealed class SearchListener : Java.Lang.Object, MediaBrowser.IListener
    {
        public int Count { get; private set; } = -1;

        public void OnSearchResultChanged(MediaBrowser? browser, string? query, int itemCount, LibraryParams? @params) => Count = itemCount;

        public void OnChildrenChanged(MediaBrowser? browser, string? parentId, int itemCount, LibraryParams? @params)
        {
        }

        public void OnAvailableSessionCommandsChanged(MediaController? controller, SessionCommands? commands)
        {
        }

        public IListenableFuture? OnCustomCommand(MediaController? controller, SessionCommand? command, Bundle? args) =>
            CarMediaFutures.Done(new SessionResult(SessionError.ErrorNotSupported));

        public void OnCustomLayoutChanged(MediaController? controller, IList<CommandButton>? layout)
        {
        }

        public void OnDisconnected(MediaController? controller)
        {
        }

        public void OnError(MediaController? controller, SessionError? sessionError)
        {
        }

        public void OnExtrasChanged(MediaController? controller, Bundle? extras)
        {
        }

        public void OnMediaButtonPreferencesChanged(MediaController? controller, IList<CommandButton>? mediaButtonPreferences)
        {
        }

        public void OnSessionActivityChanged(MediaController? controller, Android.App.PendingIntent? sessionActivity)
        {
        }

        public IListenableFuture? OnSetCustomLayout(MediaController? controller, IList<CommandButton>? layout) =>
            CarMediaFutures.Done(new SessionResult(SessionResult.ResultSuccess));
    }

    private sealed class FocusListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange)
        {
        }
    }
}
