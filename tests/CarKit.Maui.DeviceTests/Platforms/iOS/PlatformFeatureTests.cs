using CarKit.Maui.Navigation;
using CarKit.Maui.Renderers;
using CarPlay;
using Microsoft.Extensions.DependencyInjection;
using UIKit;

#pragma warning disable CARKIT0001 // the iOS 27 video list is experimental

namespace CarKit.Maui.DeviceTests;

// Stage 9: iOS 26.4 features and the iOS 18.4 sports mode on CP* objects (spec: "New platform features decisions
// (Stage 9)"). The same tests in three configurations: the 26.4 asset on iOS 26.0 (runtime gate), the 26.4 asset on
// iOS 26.4 (features), the 26.0 asset (the test app built with net10.0-ios26.0) on iOS 26.4 (asset gate). Expectations
// come from Has264.
internal static class PlatformFeatureTests
{
    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Capabilities_report_the_asset), Capabilities_report_the_asset),
        new(nameof(Voice_actions_on_the_state), Voice_actions_on_the_state),
        new(nameof(Voice_actions_update_in_place), Voice_actions_update_in_place),
        new(nameof(Voice_bar_buttons), Voice_bar_buttons),
        new(nameof(Voice_actions_gate_through_the_session), Voice_actions_gate_through_the_session),
        new(nameof(List_details_header), List_details_header),
        new(nameof(List_header_updates_in_place), List_header_updates_in_place),
        new(nameof(List_header_gate_through_the_session), List_header_gate_through_the_session),
        new(nameof(List_item_playback_configuration), List_item_playback_configuration),
        new(nameof(Video_browse_renders_a_list), Video_browse_renders_a_list),
        new(nameof(Sports_mode_on_now_playing), Sports_mode_on_now_playing),
        new(nameof(Sports_mode_with_optional_fields_missing), Sports_mode_with_optional_fields_missing),
        new(nameof(Trip_uses_waypoints), Trip_uses_waypoints),
        new(nameof(Route_segments_on_the_session), Route_segments_on_the_session),
        new(nameof(Reroute_with_segments_and_reason), Reroute_with_segments_and_reason),
        new(nameof(Route_sharing_requests_reach_the_handler), Route_sharing_requests_reach_the_handler),
    ];

    // The asset the library (and the test app) was built with, and the simulator's OS
    private static bool Asset264 =>
#if IOS26_4_OR_GREATER
        true;
#else
        false;
#endif

    // The platform analyzer treats Has264 like a version check
    [System.Runtime.Versioning.SupportedOSPlatformGuard("ios26.4")]
    private static bool Has264 => Asset264 && OperatingSystem.IsIOSVersionAtLeast(26, 4);

    private static readonly CarImage _thumbnail = CarImage.FromSystemSymbol("film", null);

    private sealed class Fixture : IDisposable
    {
        public Fixture(CarAppCategory categories = CarAppCategory.Parking)
        {
            Test = TestSession.CarPlay(categories);
            Context = new CarPlayRenderContext(
                Test.Session, Test, new CarImageLoader<UIImage>(new CarPlayImages(2), Test, combineDarkVariants: true), 2, () => CarContentStyle.Light,
                _ => Task.CompletedTask, [.. NavigationTestServices.Services.GetServices<ICarPlayModuleRenderer>()]);
        }

        public TestSession Test { get; }

        public CarPlayRenderContext Context { get; }

        public CarPlayTemplateState Render(CarTemplate template) => CarPlayTemplateRenderers.Render(template, Context);

        public CarPlayNavigationSink? Sink => Module.SinkFor(Context);

        public async Task<CarPlayNavigationSink> ConnectAsync()
        {
            await NavigationTestServices.ResetAsync();
            Module.OnConnected(Context, window: null);
            Render(NavigationTestServices.Map());
            await Check.Eventually(() => Sink is not null, "the navigation sink");
            return Sink!;
        }

        public void Dispose() => Module.OnDisconnected(Context);
    }

    private static CarPlayNavigationModule Module => NavigationTestServices.Services.GetRequiredService<CarPlayNavigationModule>();

    private static CarVoiceControlTemplate Voice(string first = "Stop") => new([
        new CarVoiceControlState("listening", "Listening") { Actions = [new CarAction(first) { Id = "a1" }, new CarAction("Pause") { Id = "a2", IsEnabled = false }] },
        new CarVoiceControlState("thinking", "Thinking"),
    ]) { Id = "voice", TrailingBarButtons = [new CarBarButton("Close") { Id = "close" }] };

    private static CarListTemplate ListWithHeader(string title = "Downtown") => new("Parking lots")
    {
        Id = "parkings",
        DetailsHeader = new CarListDetailsHeader(_thumbnail)
        {
            Title = title,
            Subtitle = "42 free",
            Body = new CarText(["Open all day, $4/h", "$4/h"]),
            Actions = [new CarAction("Navigate") { Id = "go", Image = CarImage.FromSystemSymbol("car", null) }],
            Playback = new CarPlaybackConfiguration { Presentation = CarPlaybackPresentation.Video, Action = CarPlaybackAction.Play, Duration = TimeSpan.FromMinutes(2) },
            UsesAdaptiveBackground = true,
        },
        Sections = [new CarSection([new CarListItem("P1") { Id = "p1" }])],
    };

    private static Task Capabilities_report_the_asset()
    {
        var capabilities = TestSession.CarPlay().Capabilities;
        DeviceTestRunner.Info($"asset {(Asset264 ? "net10.0-ios26.4" : "net10.0-ios (26.0)")}, iOS {capabilities.IosVersion}, features: "
            + string.Join(",", Enum.GetValues<CarFeature>().Where(capabilities.Supports)));
        Check.Equal(Has264, capabilities.Supports(CarFeature.ListDetailsHeader));
        Check.Equal(Has264, capabilities.Supports(CarFeature.VoiceControlActionButtons));
        Check.Equal(Has264, capabilities.Supports(CarFeature.RouteSharing));
        Check.True(capabilities.Supports(CarFeature.NowPlayingSportsMode));
        Check.True(!capabilities.Supports(CarFeature.NowPlayingMiniPlayerOptOut));
        // Without a connection to the car the session configuration does not report video
        using var features = new CarPlayFeatures();
        DeviceTestRunner.Info("VideoPlayback without a car: " + features.Supports(CarFeature.VideoPlayback));
        Check.True(!features.Supports(CarFeature.NowPlayingMiniPlayerOptOut));
        return Task.CompletedTask;
    }

    private static Task Voice_actions_on_the_state()
    {
        using var f = new Fixture(CarAppCategory.VoiceConversational);

        var native = Check.Is<CPVoiceControlTemplate>(f.Render(Voice()).Native);

        Check.Equal(2, native.VoiceControlStates.Length);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            var buttons = Check.NotNull(native.VoiceControlStates[0].ActionButtons);
            DeviceTestRunner.Info("CPVoiceControlState.MaximumActionButtonCount = " + CPVoiceControlState.MaximumActionButtonCount);
            Check.Equal(Math.Min(2, (int)CPVoiceControlState.MaximumActionButtonCount), buttons.Length);
            Check.Equal("Stop", buttons[0].Title);
            Check.NotNull(buttons[0].Image);
            Check.True(buttons[0].Enabled);
            Check.True(buttons.Length < 2 || !buttons[1].Enabled);
            Check.Equal(0, native.VoiceControlStates[1].ActionButtons?.Length ?? 0);
        }
#endif
        return Task.CompletedTask;
    }

    private static Task Voice_actions_update_in_place()
    {
        using var f = new Fixture(CarAppCategory.VoiceConversational);
        var state = f.Render(Voice());
        var native = state.Native;

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, Voice(first: "Next"), f.Context));

        Check.Same(native, state.Native);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal("Next", ((CPVoiceControlTemplate)native).VoiceControlStates[0].ActionButtons![0].Title);
        }
#endif
        return Task.CompletedTask;
    }

    private static Task Voice_bar_buttons()
    {
        using var f = new Fixture(CarAppCategory.VoiceConversational);

        var native = Check.Is<CPVoiceControlTemplate>(f.Render(Voice()).Native);

        // The binding marks the bar as iOS and the header as iOS 26.4; on iOS 26.0 it must not be touched
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal("Close", Check.NotNull(native.TrailingNavigationBarButtons)[0].Title);
        }
#endif
        Check.Equal(2, native.VoiceControlStates.Length);

        return Task.CompletedTask;
    }

    // Navigation: Voice Control without a minimum version in the rules (VoiceConversational requires iOS 26.4), so on
    // iOS 26.0 the modal goes through and the gate removes only the buttons
    private static async Task Voice_actions_gate_through_the_session()
    {
        using var f = new Fixture(CarAppCategory.Navigation);
        await f.Test.Session.SetRootAsync(new CarListTemplate("Assistant"));

        await f.Test.Session.PresentAsync(Voice());

        var sent = Check.Is<CarVoiceControlTemplate>(f.Test.Templates[^1]);
        Check.Equal(Has264 ? 2 : 0, sent.States[0].Actions.Count);
        Check.Equal(2, Check.Is<CarVoiceControlTemplate>(f.Test.Session.Presented).States[0].Actions.Count);
    }

    private static Task List_details_header()
    {
        using var f = new Fixture();

        var native = Check.Is<CPListTemplate>(f.Render(ListWithHeader()).Native);

        Check.Equal(1, native.Sections.Length);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            var header = Check.NotNull(native.ListHeader);
            Check.Equal("Downtown", header.Title);
            Check.Equal("42 free", header.Subtitle);
            Check.Equal("Open all day, $4/h", header.BodyVariants[0].Value);
            Check.Equal(2, header.BodyVariants.Length);
            Check.True(header.AdaptiveBackgroundStyle);
            Check.NotNull(header.Thumbnail.Image);
            Check.Equal("Navigate", header.ActionButtons[0].Title);
            var playback = Check.NotNull(header.PlaybackConfiguration);
            Check.Equal(CPPlaybackPresentation.Video, playback.PreferredPresentation);
            DeviceTestRunner.Info("header playback action (Play, 0 s, 120 s) → " + playback.PlaybackAction);
            foreach (var action in new[] { CPPlaybackAction.None, CPPlaybackAction.Play, CPPlaybackAction.Pause, CPPlaybackAction.Replay })
            {
                header.PlaybackConfiguration = new CPPlaybackConfiguration(CPPlaybackPresentation.Audio, action, CoreMedia.CMTime.FromSeconds(10, 1000), CoreMedia.CMTime.FromSeconds(120, 1000));
                DeviceTestRunner.Info($"probe header.PlaybackConfiguration = {action} → {header.PlaybackConfiguration?.PlaybackAction}");
            }
            Check.Equal(120d, Math.Round(playback.Duration.Seconds));
        }
#endif
        return Task.CompletedTask;
    }

    private static Task List_header_updates_in_place()
    {
        using var f = new Fixture();
        var state = f.Render(ListWithHeader());
        var native = (CPListTemplate)state.Native;

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, ListWithHeader("Gallery"), f.Context));
        Check.Same(native, state.Native);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal("Gallery", native.ListHeader?.Title);
            Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, ListWithHeader() with { DetailsHeader = null }, f.Context));
            Check.Null(native.ListHeader);
        }
#endif
        return Task.CompletedTask;
    }

    private static async Task List_header_gate_through_the_session()
    {
        using var f = new Fixture();

        await f.Test.Session.SetRootAsync(ListWithHeader());

        var sent = Check.Is<CarListTemplate>(f.Test.Templates[^1]);
        Check.Equal(Has264, sent.DetailsHeader is not null);
        Check.True(f.Test.Logs.Any(log => log.Contains("ListDetailsHeader", StringComparison.Ordinal)) != Has264);
    }

    private static Task List_item_playback_configuration()
    {
        using var f = new Fixture();
        var list = new CarListTemplate("Episodes")
        {
            Sections = [new CarSection([new CarListItem("Episode 1") { Id = "e1", Playback = new CarPlaybackConfiguration { Action = CarPlaybackAction.Pause, Elapsed = TimeSpan.FromSeconds(30), Duration = TimeSpan.FromMinutes(3) } }])],
        };
        var state = f.Render(list);
        var item = ((CPListTemplate)state.Native).Sections[0].Item<CPListItem>(0);

#if IOS26_4_OR_GREATER
        if (Has264)
        {
            var playback = Check.NotNull(item.PlaybackConfiguration);
            Check.Equal(CPPlaybackAction.Pause, playback.PlaybackAction);
            Check.Equal(30d, Math.Round(playback.ElapsedTime.Seconds));
            Check.Equal(180d, Math.Round(playback.Duration.Seconds));
            CarPlayTemplateRenderers.Update(state, list with { Sections = [new CarSection([new CarListItem("Episode 1") { Id = "e1" }])] }, f.Context);
            // The binding does not accept null: a removed state = None/None
            Check.Equal(CPPlaybackAction.None, Check.NotNull(item.PlaybackConfiguration).PlaybackAction);
            Check.Equal(CPPlaybackPresentation.None, item.PlaybackConfiguration.PreferredPresentation);
            foreach (var (action, elapsed, duration) in new[] { (CPPlaybackAction.Play, 0d, 120d), (CPPlaybackAction.Play, 10d, 120d), (CPPlaybackAction.Play, 0d, 0d), (CPPlaybackAction.Replay, 0d, 120d) })
            {
                var probe = new CPPlaybackConfiguration(CPPlaybackPresentation.Video, action, CoreMedia.CMTime.FromSeconds(elapsed, 1000), CoreMedia.CMTime.FromSeconds(duration, 1000));
                DeviceTestRunner.Info($"probe CPPlaybackConfiguration({action}, {elapsed} s, {duration} s) → {probe.PlaybackAction}, {probe.PreferredPresentation}");
            }
        }
#endif
        Check.Equal("Episode 1", item.Text);
        return Task.CompletedTask;
    }

    private static Task Video_browse_renders_a_list()
    {
        using var f = new Fixture(CarAppCategory.Video);
        var video = new CarPlayVideoBrowseTemplate("Movies")
        {
            Id = "videos",
            DetailsHeader = new CarListDetailsHeader(_thumbnail) { Title = "New releases" },
            Sections = [new CarSection([new CarListItem("Movie") { Id = "film", Playback = new CarPlaybackConfiguration { Presentation = CarPlaybackPresentation.Video } }])],
        };

        var state = f.Render(video);

        var native = Check.Is<CPListTemplate>(state.Native);
        Check.Equal("videos", native.UserInfo?.ToString());
        Check.Same(video, state.Model);
        Check.Equal(1, native.Sections[0].Items2.Length);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal("New releases", native.ListHeader?.Title);
            Check.Equal(CPPlaybackPresentation.Video, native.Sections[0].Item<CPListItem>(0).PlaybackConfiguration?.PreferredPresentation);
        }
#endif
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, video with { DetailsHeader = new CarListDetailsHeader(_thumbnail) { Title = "Hits" } }, f.Context));
        return Task.CompletedTask;
    }

    private static Task Sports_mode_on_now_playing()
    {
        using var f = new Fixture(CarAppCategory.Audio);
        var match = new CarNowPlayingTemplate
        {
            Id = "np",
            Sports = new CarNowPlayingSports(new CarSportsTeam("Legends Wellington") { Score = "2", IsFavorite = true }, new CarSportsTeam("Lakers Portland") { Score = "1" })
            {
                Status = "2nd half",
                Clock = new CarSportsClock(TimeSpan.FromMinutes(67), countsUp: true),
            },
        };

        var state = f.Render(match);

        var native = Check.Is<CPNowPlayingTemplate>(state.Native);
        if (!OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            return Task.CompletedTask;
        }

        var sports = Check.Is<CPNowPlayingModeSports>(native.NowPlayingMode);
        Check.Equal("Legends Wellington", sports.LeftTeam.Name);
        Check.Equal("LEG", sports.LeftTeam.Logo.Initials);
        Check.Equal("2", sports.LeftTeam.EventScore);
        Check.True(sports.LeftTeam.Favorite);
        var status = Check.NotNull(sports.EventStatus);
        Check.Equal("2nd half", status.EventStatusText![0]);
        Check.Equal(4020d, Check.NotNull(status.EventClock).TimeValue);
        CarPlayTemplateRenderers.Update(state, match with { Sports = null }, f.Context);
        Check.True(native.NowPlayingMode is not CPNowPlayingModeSports);
        return Task.CompletedTask;
    }

    // Review (I1): optional model fields (score, standings, logo, possession indicator, status, clock, background): the
    // binding checks null on some constructor arguments, so a model without them must still render
    private static Task Sports_mode_with_optional_fields_missing()
    {
        using var f = new Fixture(CarAppCategory.Audio);
        var before = new CarNowPlayingTemplate
        {
            Id = "np-before",
            Sports = new CarNowPlayingSports(new CarSportsTeam("Legends Wellington"), new CarSportsTeam("Lakers Portland")) { Status = "Before the match" },
        };

        var state = f.Render(before);

        if (!OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            return Task.CompletedTask;
        }

        var native = Check.Is<CPNowPlayingTemplate>(state.Native);
        var sports = Check.Is<CPNowPlayingModeSports>(native.NowPlayingMode);
        Check.Equal("LEG", sports.LeftTeam.Logo.Initials);
        // CarPlay turns an empty score into "0" (a Stage 9 review probe)
        DeviceTestRunner.Info("sports team without a score → EventScore " + sports.LeftTeam.EventScore);
        Check.NotNull(sports.LeftTeam.EventScore);
        Check.Null(sports.EventStatus?.EventClock);
        CarPlayTemplateRenderers.Update(state, before with { Sports = new CarNowPlayingSports(new CarSportsTeam("A"), new CarSportsTeam("B")) }, f.Context);
        Check.Null(Check.Is<CPNowPlayingModeSports>(native.NowPlayingMode).EventStatus);
        CarPlayTemplateRenderers.Update(state, before with { Sports = null }, f.Context);
        return Task.CompletedTask;
    }

    private static async Task Trip_uses_waypoints()
    {
        using var f = new Fixture(CarAppCategory.Navigation);
        var sink = await f.ConnectAsync();
        var trip = NavigationTestServices.Trip() with { IsDestinationShareable = true };

        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);

        var native = Check.NotNull(sink.Trips.GetValueOrDefault("work"));
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal("Work", native.DestinationWaypoint.Name);
            Check.Equal(52.1672, Math.Round(native.DestinationWaypoint.CenterPoint.Latitude, 4));
            Check.Equal(52.2297, Math.Round(native.OriginWaypoint.CenterPoint.Latitude, 4));
            Check.True(native.HasShareableDestination);
        }
#endif
        Check.NotNull(sink.Session);
        await NavigationTestServices.ResetAsync();
    }

    private static CarRouteChoice TwoLegs(string id) => new("Via the charger")
    {
        Id = id,
        Estimates = new CarTravelEstimates(new CarDistance(12, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(25)),
        Maneuvers = NavigationTestServices.Trip().Routes[0].Maneuvers,
        Segments =
        [
            new CarRouteSegment(new CarWaypoint(new CarLocation(52.2297, 21.0122)), new CarWaypoint(new CarLocation(52.20, 21.00)) { Name = "Charger" })
            {
                Path = [new CarLocation(52.2297, 21.0122), new CarLocation(52.20, 21.00)],
                Maneuvers = NavigationTestServices.Trip().Routes[0].Maneuvers,
                Estimates = new CarTravelEstimates(new CarDistance(5, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(10)),
            },
            new CarRouteSegment(new CarWaypoint(new CarLocation(52.20, 21.00)) { Name = "Charger" }, new CarWaypoint(new CarLocation(52.1672, 20.9679)) { Name = "Work" })
            {
                Path = [new CarLocation(52.20, 21.00), new CarLocation(52.18, 20.98), new CarLocation(52.1672, 20.9679)],
                Estimates = new CarTravelEstimates(new CarDistance(7, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(15)),
            },
        ],
    };

    private static CarTrip StopTrip() => new(new CarLocation(52.2297, 21.0122), new CarLocation(52.1672, 20.9679), [TwoLegs("stop")]) { Id = "stop-trip", DestinationName = "Work" };

    private static async Task Route_segments_on_the_session()
    {
        using var f = new Fixture(CarAppCategory.Navigation);
        var sink = await f.ConnectAsync();
        var trip = StopTrip();

        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        await NavigationTestServices.Navigation.UpdateCurrentSegmentAsync(1);

        var session = Check.NotNull(sink.Session);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal(2, sink.RouteSegments.Length);
            DeviceTestRunner.Info($"CPNavigationSession.RouteSegments without CarPlay: {session.RouteSegments?.Length}");
            Check.Equal("Charger", sink.RouteSegments[0].Destination.Name);
            Check.Equal(2, (int)sink.RouteSegments[0].CoordinatesCount);
            Check.Equal(3, (int)sink.RouteSegments[1].CoordinatesCount);
            Check.Equal(2, sink.RouteSegments[0].CurrentManeuvers.Length);
            Check.True(Check.NotNull(session.CurrentSegment).Identifier.Equals(sink.RouteSegments[1].Identifier));
        }
#endif
        Check.Equal(2, session.UpcomingManeuvers.Length);
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Reroute_with_segments_and_reason()
    {
        using var f = new Fixture(CarAppCategory.Navigation);
        var sink = await f.ConnectAsync();
        var trip = StopTrip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        await NavigationTestServices.Navigation.UpdateCurrentSegmentAsync(1);

        await NavigationTestServices.Navigation.RerouteAsync(TwoLegs("detour"), CarRerouteReason.WaypointModified);

        var session = Check.NotNull(sink.Session);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            Check.Equal(2, sink.RouteSegments.Length);
            Check.True(Check.NotNull(session.CurrentSegment).Identifier.Equals(sink.RouteSegments[0].Identifier));
        }
#endif
        Check.Equal(0, NavigationTestServices.Navigation.CurrentSegmentIndex);
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Route_sharing_requests_reach_the_handler()
    {
        using var f = new Fixture(CarAppCategory.Navigation);
        var sink = await f.ConnectAsync();
        var trip = StopTrip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
#if IOS26_4_OR_GREATER
        if (Has264)
        {
            var map = Check.Is<CPMapTemplate>(f.Render(NavigationTestServices.Map()).Native);
            var mapDelegate = (CPMapTemplateDelegate)map.MapDelegate!;
            var charger = CarPlayNavigationModels.Waypoint(new CarWaypoint(new CarLocation(52.21, 21.01)) { Name = "Charger 2" });
            var completed = new TaskCompletionSource<CPTravelEstimates>();

            // The test app does not enable nav.ShareRoutesWithCar
            Check.True(!mapDelegate.ShouldProvideRouteSharing(map));
            mapDelegate.DidRequestToInsertWaypoint(map, charger, sink.RouteSegments[1], estimates => completed.TrySetResult(estimates));
            var estimates = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            mapDelegate.DidReceiveUpdatedRouteSource(map, CPRouteSource.Vehicle);
            mapDelegate.DidReceiveRequestForDestination(map, charger);
            mapDelegate.MapTemplateWaypoint(map, charger, true, null);

            Check.Equal(2d, estimates.DistanceRemaining.DoubleValue);
            await Check.Eventually(() => NavigationTestServices.Handler.Calls.Count >= 4, "route sharing handler calls");
            Check.Equal("insert:Charger 2@1,source:Vehicle,destination:Charger 2,answer:Charger 2:True@", string.Join(',', NavigationTestServices.Handler.Calls));
        }
#endif
        Check.NotNull(sink.Session);
        await NavigationTestServices.ResetAsync();
    }
}
