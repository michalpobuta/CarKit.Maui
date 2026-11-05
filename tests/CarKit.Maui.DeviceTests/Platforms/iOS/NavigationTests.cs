using AVFoundation;
using CarKit.Maui.Navigation;
using CarKit.Maui.Renderers;
using CarPlay;
using CoreGraphics;
using Foundation;
using MapKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using UIKit;

namespace CarKit.Maui.DeviceTests;

// Stage 8: the Navigation module on CarPlay without a CarPlay connection (spec: "Navigation module decisions (Stage 8)"):
// CPMapTemplate from the real renderer, a navigation session on CP* objects, the map delegate via the CarPlay path,
// surfaces in a stand-in window (pixels), Dashboard and cluster through the module, AVSpeechSynthesizer with ducking,
// frame times
internal static class NavigationTests
{
    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Map_template_is_created_and_configured), Map_template_is_created_and_configured),
        new(nameof(Map_template_updates_in_place_with_hooks), Map_template_updates_in_place_with_hooks),
        new(nameof(Navigation_session_without_carplay_connection), Navigation_session_without_carplay_connection),
        new(nameof(Trip_previews_and_maneuvers_map_to_cp_objects), Trip_previews_and_maneuvers_map_to_cp_objects),
        new(nameof(Reroute_pause_finish_and_alert), Reroute_pause_finish_and_alert),
        new(nameof(Map_delegate_routes_gestures_and_driver_actions), Map_delegate_routes_gestures_and_driver_actions),
        new(nameof(Background_maneuver_and_alert_selection), Background_maneuver_and_alert_selection),
        new(nameof(Surface_custom_drawing_pixels), Surface_custom_drawing_pixels),
        new(nameof(Surface_native_view_controller_pixels), Surface_native_view_controller_pixels),
        new(nameof(Surface_maui_view_pixels), Surface_maui_view_pixels),
        new(nameof(Dashboard_and_cluster_surfaces), Dashboard_and_cluster_surfaces),
        new(nameof(Voice_prompt_speaks_with_ducking_and_restores_the_session), Voice_prompt_speaks_with_ducking_and_restores_the_session),
        new(nameof(Frame_times_custom_and_native), Frame_times_custom_and_native),
    ];

    private static CarPlayNavigationModule Module => NavigationTestServices.Services.GetRequiredService<CarPlayNavigationModule>();

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Context = new CarPlayRenderContext(
                Test.Session, Test, new CarImageLoader<UIImage>(new CarPlayImages(2), Test, combineDarkVariants: true), 2, () => CarContentStyle.Light,
                _ => Task.CompletedTask, [.. NavigationTestServices.Services.GetServices<ICarPlayModuleRenderer>()]);
        }

        public TestSession Test { get; } = TestSession.CarPlay(CarAppCategory.Navigation);

        public CarPlayRenderContext Context { get; }

        public CarPlayTemplateState Render(CarTemplate template) => CarPlayTemplateRenderers.Render(template, Context);

        // A CarPlay connection without a window (the surface tests cover the window) with a map template
        public async Task<(CPMapTemplate Map, CarPlayNavigationSink Sink)> ConnectAsync()
        {
            await NavigationTestServices.ResetAsync();
            Module.OnConnected(Context, window: null);
            var map = (CPMapTemplate)Render(NavigationTestServices.Map()).Native;
            await Check.Eventually(() => Sink(Context) is not null, "the navigation sink");
            return (map, Sink(Context)!);
        }

        public void Dispose() => Module.OnDisconnected(Context);

        private static CarPlayNavigationSink? Sink(CarPlayRenderContext context) => Module.SinkFor(context);
    }

    private static async Task Map_template_is_created_and_configured()
    {
        var f = new Fixture();
        var pressed = 0;
        var state = f.Render(NavigationTestServices.Map(_ =>
        {
            pressed++;
            return Task.CompletedTask;
        }));

        var map = Check.Is<CPMapTemplate>(state.Native);
        Check.Equal("map", map.UserInfo?.ToString());
        Check.Equal(3, map.MapButtons.Length);
        Check.True(map.MapButtons[0].Enabled);
        Check.True(!map.MapButtons[1].Enabled);
        Check.True(map.MapButtons[2].Hidden);
        Check.NotNull(map.MapButtons[0].Image);
        Check.Equal(1, map.TrailingNavigationBarButtons.Length);
        Check.Equal("Destination", map.TrailingNavigationBarButtons[0].Title);
        Check.Equal(1, map.LeadingNavigationBarButtons.Length);
        Check.True(!map.AutomaticallyHidesNavigationBar);
        Check.NotNull(map.GuidanceBackgroundColor);
        Check.Equal(CPTripEstimateStyle.Light, map.TripEstimateStyle);
        Check.NotNull(map.MapDelegate);

        // Map button: the CPMapButton handler → the model's OnSelected
        MapTemplateRenderer.Press(state, ((CarMapTemplate)state.Model).MapButtons[0], f.Context);
        await Check.Eventually(() => pressed == 1, "the map button handler");
        Check.Equal(0, f.Test.Errors.Count);
    }

    private static Task Map_template_updates_in_place_with_hooks()
    {
        var f = new Fixture();
        var hooked = 0;
        var model = NavigationTestServices.Map().ConfigureCarPlay(map =>
        {
            hooked++;
            map.HidesButtonsWithNavigationBar = true;
        });
        var state = f.Render(model);
        var native = (CPMapTemplate)state.Native;

        var updated = model with { MapButtons = [model.MapButtons[0]], AutomaticallyHidesNavigationBar = true };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, f.Context));

        Check.Same(native, state.Native);
        Check.Equal(1, native.MapButtons.Length);
        Check.True(native.AutomaticallyHidesNavigationBar);
        Check.True(native.HidesButtonsWithNavigationBar);
        Check.Equal(2, hooked);
        return Task.CompletedTask;
    }

    // Probe: what works on CPMapTemplate and CPNavigationSession without a CarPlay connection (the result goes to the log
    // and the ledger)
    private static async Task Navigation_session_without_carplay_connection()
    {
        using var f = new Fixture();
        var (map, sink) = await f.ConnectAsync();
        var trip = NavigationTestServices.Trip();

        await NavigationTestServices.Navigation.ShowTripPreviewsAsync([trip], trip);
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);

        DeviceTestRunner.Info(FormattableString.Invariant($"probe CPMapTemplate.StartNavigationSession without CarPlay: session={(sink.Session is null ? "null" : sink.Session.GetType().Name)}, ")
            + FormattableString.Invariant($"Trip={sink.Session?.Trip?.UserInfo?.ToString() ?? "null"}, upcoming={sink.Session?.UpcomingManeuvers?.Length}, PanningInterfaceVisible={map.PanningInterfaceVisible}"));
        var session = Check.NotNull(sink.Session);
        Check.Equal("work", session.Trip.UserInfo?.ToString());
        Check.Equal(2, session.UpcomingManeuvers.Length);
    }

    private static async Task Trip_previews_and_maneuvers_map_to_cp_objects()
    {
        using var f = new Fixture();
        var (_, sink) = await f.ConnectAsync();
        var trip = NavigationTestServices.Trip();

        await NavigationTestServices.Navigation.ShowTripPreviewsAsync([trip], trip);
        var native = Check.NotNull(sink.Trips.GetValueOrDefault("work"));
        Check.Equal(2, native.RouteChoices.Length);
        var route = native.RouteChoices[0];
        Check.Equal("fast", route.UserInfo?.ToString());
        Check.Equal("Via Riverside Drive", route.SummaryVariants![0]);
        Check.Equal("Fastest", route.AdditionalInformationVariants![0]);
        Check.Equal("25 min", route.SelectionSummaryVariants![0]);
        if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            Check.Equal("Work", native.DestinationNameVariants![0]);
        }

        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        var session = Check.NotNull(sink.Session);
        var first = session.UpcomingManeuvers[0];
        Check.Equal("m1", first.UserInfo?.ToString());
        Check.Equal("Turn right onto Main Street", first.InstructionVariants![0]);
        Check.Equal("Right", first.InstructionVariants[1]);
        Check.NotNull(first.SymbolImage);
        var initial = Check.NotNull(first.InitialTravelEstimates);
        Check.Equal(300d, initial.DistanceRemaining.DoubleValue);
        Check.Equal(40d, initial.TimeRemaining);
        if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            Check.Equal(CPManeuverType.RightTurn, first.ManeuverType);
            Check.Equal("Main Street", first.RoadFollowingManeuverVariants![0]);
            var guidance = Check.NotNull(first.LinkedLaneGuidance);
            Check.Equal(2, guidance.Lanes!.Length);
            Check.Equal(CPManeuverType.RoundaboutExit3, session.UpcomingManeuvers[1].ManeuverType);
            Check.Same(guidance, session.CurrentLaneGuidance);
            if (OperatingSystem.IsIOSVersionAtLeast(18))
            {
                Check.Equal(2, guidance.Lanes[1].Angles!.Length);
                Check.Equal(90d, guidance.Lanes[1].Angles[1].DoubleValue);
                // I5 (review): the recommended lane direction: highlighted angle and Preferred status; a lane without a
                // recommendation: NotGood
                Check.Equal(90d, Check.NotNull(guidance.Lanes[1].HighlightedAngle).DoubleValue);
                Check.Equal(CPLaneStatus.Preferred, guidance.Lanes[1].Status);
                Check.Null(guidance.Lanes[0].HighlightedAngle);
                Check.Equal(CPLaneStatus.NotGood, guidance.Lanes[0].Status);
            }
        }

        // Maneuver estimates: the session (current maneuver) and the template (destination)
        await NavigationTestServices.Navigation.UpdateEstimatesAsync(
            new CarTravelEstimates(new CarDistance(11, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(22)),
            new CarTravelEstimates(new CarDistance(120, CarDistanceUnit.Meters), TimeSpan.FromSeconds(15)));
        await NavigationTestServices.Navigation.UpdateManeuversAsync([trip.Routes[0].Maneuvers[1]]);
        Check.Equal(1, session.UpcomingManeuvers.Length);
        Check.Equal("m2", session.UpcomingManeuvers[0].UserInfo?.ToString());
        if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            // M7 (review): a maneuver without lanes does not leave the previous maneuver's lanes on screen
            Check.Null(session.CurrentLaneGuidance);
        }
        Check.Equal(0, f.Test.Errors.Count);
    }

    private static async Task Reroute_pause_finish_and_alert()
    {
        using var f = new Fixture();
        var (map, sink) = await f.ConnectAsync();
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        var session = Check.NotNull(sink.Session);

        await NavigationTestServices.Navigation.PauseAsync(CarNavigationPauseReason.Rerouting, "Calculating a detour");
        var detour = new CarRouteChoice("Detour") { Id = "detour", Maneuvers = [new CarManeuver("Turn left") { Id = "d1", Kind = CarManeuverKind.Left }] };
        await NavigationTestServices.Navigation.RerouteAsync(detour);
        Check.Equal("d1", session.UpcomingManeuvers[0].UserInfo?.ToString());

        var selected = 0;
        var alert = new CarNavigationAlert("Traffic jam", new CarAction("Detour", _ =>
        {
            selected++;
            return Task.CompletedTask;
        }) { Id = "alert-primary" })
        { Id = "alert", Subtitle = "In 2 km", SecondaryAction = new CarAction("Stay") { Id = "alert-secondary" } };
        await NavigationTestServices.Navigation.ShowAlertAsync(alert);
        DeviceTestRunner.Info($"probe PresentNavigationAlert without CarPlay: CurrentNavigationAlert={(map.CurrentNavigationAlert is null ? "null" : "set")}");
        if (map.CurrentNavigationAlert is { } shown)
        {
            Check.Equal("Traffic jam", shown.TitleVariants[0]);
            Check.Equal("alert", Module.AlertId(shown));
            // I4 (review): CarPlay hands the alert back in the delegate as a native object; after a GC the managed peer may
            // be new
            var handle = shown.Handle;
            shown = null;
            ForceCollection();
            var again = Check.NotNull(ObjCRuntime.Runtime.GetNSObject<CPNavigationAlert>(handle));
            Check.Equal("alert", Module.AlertId(again));
            ((CPMapTemplateDelegate)map.MapDelegate!).DidDismissNavigationAlert(map, again, CPNavigationAlertDismissalContext.Timeout);
            Check.Null(Module.AlertId(again));
        }

        await NavigationTestServices.Navigation.FinishAsync();
        Check.Null(sink.Session);
        Check.Equal(CarNavigationState.Idle, NavigationTestServices.Navigation.State);
        Check.Equal(0, f.Test.Errors.Count);
    }

    private static async Task Map_delegate_routes_gestures_and_driver_actions()
    {
        using var f = new Fixture();
        var (map, sink) = await f.ConnectAsync();
        var mapDelegate = (CPMapTemplateDelegate)map.MapDelegate!;
        var gestures = new List<CarMapGestureEventArgs>();
        var surface = NavigationTestServices.Runtime.Surfaces.CarScreen;
        void OnGesture(object? sender, CarMapGestureEventArgs e) => gestures.Add(e);
        surface.Gesture += OnGesture;
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.ShowTripPreviewsAsync([trip], null);
        var native = sink.Trips["work"];

        try
        {
            mapDelegate.DidBeginPanGesture(map);
            mapDelegate.DidUpdatePanGesture(map, new CGPoint(-20, 5), CGPoint.Empty);
            mapDelegate.DidEndPanGesture(map, new CGPoint(-400, 0));
            if (OperatingSystem.IsIOSVersionAtLeast(26))
            {
                mapDelegate.DidBeginZoomGesture(map);
                mapDelegate.DidUpdateZoomGesture(map, new CGPoint(100, 50), 2, 0);
            }

            mapDelegate.DidShowPanningInterface(map);
            Check.True(surface.IsPanning);
            mapDelegate.DidDismissPanningInterface(map);
            mapDelegate.SelectedPreview(map, native, native.RouteChoices[1]);
            mapDelegate.StartedTrip(map, native, native.RouteChoices[0]);
            await Check.Eventually(() => NavigationTestServices.Handler.Calls.Count == 2, "the preview and start handlers");
            Check.True(mapDelegate.ShouldShowNotificationForManeuver(map, new CPManeuver()));
            if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
            {
                Check.True(mapDelegate.ShouldProvideNavigationMetadata(map));
            }

            await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
            mapDelegate.DidCancelNavigation(map);
            await Check.Eventually(() => NavigationTestServices.Navigation.State == CarNavigationState.Idle, "the car ending navigation");
            await Check.Eventually(() => NavigationTestServices.Handler.Calls.Contains("ended"), "the ended handler");
        }
        finally
        {
            surface.Gesture -= OnGesture;
        }

        Check.Equal(CarMapGestureKind.Scroll, gestures[0].Kind);
        Check.Equal(new PointF(20, -5), gestures[0].Delta);
        Check.Equal(CarMapGestureKind.Fling, gestures[1].Kind);
        Check.Equal(new PointF(-400, 0), gestures[1].Velocity);
        if (OperatingSystem.IsIOSVersionAtLeast(26))
        {
            Check.Equal(CarMapGestureKind.Scale, gestures[2].Kind);
            Check.Equal(2f, gestures[2].ScaleFactor);
        }
        Check.Equal("preview:work/eco,start:work/fast,ended", string.Join(',', NavigationTestServices.Handler.Calls));
        Check.Null(sink.Session);
    }

    private static async Task Background_maneuver_and_alert_selection()
    {
        using var f = new Fixture();
        var (_, sink) = await f.ConnectAsync();
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);

        Module.OnManeuverSelected(sink.Upcoming[0]);
        await Check.Eventually(() => NavigationTestServices.Handler.Calls.Contains("maneuver:m1"), "the maneuver handler");
    }

    private static void ForceCollection()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // A stand-in window for CPWindow in the test app's scene (no CarPlay); pixels from the window layer
    private static UIWindow Window(nfloat width, nfloat height)
    {
        var scene = UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>().First();
        return new UIWindow(scene) { Frame = new CGRect(0, 0, width, height), Hidden = false, WindowLevel = UIWindowLevel.Normal - 1 };
    }

    private static (byte R, byte G, byte B) Pixel(UIWindow window, nfloat x, nfloat y)
    {
        window.LayoutIfNeeded();
        foreach (var view in window.Subviews)
        {
            view.LayoutIfNeeded();
        }

        var data = new byte[4];
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(data, 1, 1, 8, 4, colorSpace, CGImageAlphaInfo.PremultipliedLast);
        context.TranslateCTM(-x, -(window.Bounds.Height - y - 1));
        context.TranslateCTM(0, window.Bounds.Height);
        context.ScaleCTM(1, -1);
        window.Layer.RenderInContext(context);
        return (data[0], data[1], data[2]);
    }

    private static async Task Surface_custom_drawing_pixels()
    {
        var window = Window(400, 240);
        var surface = NavigationTestServices.Runtime.Surfaces.Dashboard;
        var renderer = new SolidRenderer(Colors.Red);
        surface.UseRenderer(renderer);
        var host = new CarSurfaceWindowHost(window, surface, NavigationTestServices.Runtime, CarContentStyle.Light);
        try
        {
            host.Attach();
            Check.True(surface.IsAvailable);
            await Check.Eventually(() => surface.Size.Width == 400, "the surface size");
            surface.Invalidate();
            await Task.Delay(100);
            var (r, g, b) = Pixel(window, 200, 200);
            Check.True(renderer.Frames > 0);
            Check.True(r > 200 && g < 60 && b < 60, $"pixel {r},{g},{b} is red");
            Check.Equal(400f, renderer.Visible.Width);
            Check.True(surface.FrameStatistics.FrameCount > 0);
        }
        finally
        {
            host.Detach();
            surface.ClearContent();
            window.Hidden = true;
        }

        Check.True(!surface.IsAvailable);
    }

    private static async Task Surface_native_view_controller_pixels()
    {
        var window = Window(400, 240);
        var surface = NavigationTestServices.Runtime.Surfaces.Dashboard;
        surface.UseViewController(() =>
        {
            var controller = new UIViewController();
            controller.View!.BackgroundColor = UIColor.Blue;
            return controller;
        });
        var host = new CarSurfaceWindowHost(window, surface, NavigationTestServices.Runtime, CarContentStyle.Light);
        try
        {
            host.Attach();
            await Task.Delay(300);
            var (r, g, b) = Pixel(window, 100, 100);
            Check.True(b > 200 && r < 60 && g < 60, $"pixel {r},{g},{b} is blue");
            Check.True(surface.FrameStatistics.FrameCount > 0);
        }
        finally
        {
            host.Detach();
            surface.ClearContent();
            window.Hidden = true;
        }
    }

    private static async Task Surface_maui_view_pixels()
    {
        var window = Window(400, 240);
        var surface = NavigationTestServices.Runtime.Surfaces.Dashboard;
#pragma warning disable CARKIT0002 // experimental mode, the test checks it on purpose
        surface.UseMauiView(() => new Microsoft.Maui.Controls.BoxView { Color = Colors.Lime });
#pragma warning restore CARKIT0002
        var host = new CarSurfaceWindowHost(window, surface, NavigationTestServices.Runtime, CarContentStyle.Light);
        try
        {
            host.Attach();
            await Task.Delay(300);
            var (r, g, b) = Pixel(window, 100, 100);
            Check.True(g > 200 && r < 60 && b < 60, $"pixel {r},{g},{b} is green");
        }
        finally
        {
            host.Detach();
            surface.ClearContent();
            window.Hidden = true;
        }
    }

    private static async Task Dashboard_and_cluster_surfaces()
    {
        var dashboardWindow = Window(300, 200);
        var clusterWindow = Window(240, 160);
        var surfaces = NavigationTestServices.Runtime.Surfaces;
        surfaces.Dashboard.UseRenderer(new SolidRenderer(Colors.Red));
        surfaces.InstrumentCluster.UseRenderer(new SolidRenderer(Colors.Blue));
        var gestures = new List<CarMapGestureEventArgs>();
        void OnGesture(object? sender, CarMapGestureEventArgs e) => gestures.Add(e);
        surfaces.InstrumentCluster.Gesture += OnGesture;
        try
        {
            Module.OnDashboardConnected(null!, dashboardWindow);
            Module.OnClusterWindow(clusterWindow);
            Check.True(surfaces.Dashboard.IsAvailable);
            Check.True(surfaces.InstrumentCluster.IsAvailable);
            await Task.Delay(100);
            var (dr, _, _) = Pixel(dashboardWindow, 150, 100);
            var (_, _, cb) = Pixel(clusterWindow, 120, 80);
            Check.True(dr > 200, $"dashboard red {dr}");
            Check.True(cb > 200, $"cluster blue {cb}");
            Module.ZoomCluster(zoomIn: true);
            Module.ZoomCluster(zoomIn: false);
        }
        finally
        {
            surfaces.InstrumentCluster.Gesture -= OnGesture;
            Module.OnDashboardDisconnected(dashboardWindow);
            Module.OnClusterWindowGone();
            surfaces.Dashboard.ClearContent();
            surfaces.InstrumentCluster.ClearContent();
            dashboardWindow.Hidden = true;
            clusterWindow.Hidden = true;
        }

        Check.True(!surfaces.Dashboard.IsAvailable);
        Check.True(!surfaces.InstrumentCluster.IsAvailable);
        Check.Equal(2, gestures.Count);
        Check.Equal(2f, gestures[0].ScaleFactor);
        Check.Equal(0.5f, gestures[1].ScaleFactor);
        Check.Equal(new PointF(120, 80), gestures[0].Position);
    }

    private static async Task Voice_prompt_speaks_with_ducking_and_restores_the_session()
    {
        var voice = NavigationTestServices.Services.GetRequiredService<ICarVoicePrompts>();
        var ducking = (CarAudioDucking)NavigationTestServices.Services.GetRequiredService<ICarAudioDucking>();
        var session = AVAudioSession.SharedInstance();
        var before = (session.Category.ToString(), session.Mode.ToString());
        string? modeWhileSpeaking = null;
        var speaking = voice.SpeakAsync("In three hundred meters turn right");
        await Check.Eventually(() => voice.IsSpeaking, "the prompt to start");
        modeWhileSpeaking = session.Mode.ToString();
        Check.True(ducking.Changed);
        DeviceTestRunner.Info($"voice prompt: category {session.Category}, mode {modeWhileSpeaking}, options {session.CategoryOptions}");
        await speaking.WaitAsync(TimeSpan.FromSeconds(10));
        await Check.Eventually(() => !ducking.Changed, "the session to be restored");

        Check.Equal(AVAudioSessionMode.VoicePrompt.GetConstant()?.ToString(), modeWhileSpeaking);
        Check.Equal(before, (session.Category.ToString(), session.Mode.ToString()));

        // The Media module is playing: the prompt without a session change
        CarAudioSessionCoordinator.MediaActive = true;
        try
        {
            var second = voice.SpeakAsync("Turn right");
            await Check.Eventually(() => voice.IsSpeaking, "the second prompt");
            Check.True(!ducking.Changed);
            Check.Equal(before, (session.Category.ToString(), session.Mode.ToString()));
            voice.CancelAll();
            var cancelled = false;
            try
            {
                await second;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Check.True(cancelled);
        }
        finally
        {
            CarAudioSessionCoordinator.MediaActive = false;
        }

        // M10 (review): Media starts playing during a series; the end of the series neither restores the category nor
        // deactivates the Media session
        Check.True(ducking.Begin());
        Check.Equal(AVAudioSessionMode.VoicePrompt.GetConstant()?.ToString(), session.Mode.ToString());
        CarAudioSessionCoordinator.MediaActive = true;
        try
        {
            ducking.End();
            Check.True(!ducking.Changed);
            Check.Equal(AVAudioSessionMode.VoicePrompt.GetConstant()?.ToString(), session.Mode.ToString());
        }
        finally
        {
            CarAudioSessionCoordinator.MediaActive = false;
            session.SetCategory(before.Item1, before.Item2, default(AVAudioSessionCategoryOptions), out _);
            session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
        }
    }

    // Smoothness measurement (spec: Stage 8 acceptance): custom drawing of 120 frames and an MKMapView animated for 2 s in
    // a stand-in window
    private static async Task Frame_times_custom_and_native()
    {
        var window = Window(800, 480);
        var surface = NavigationTestServices.Runtime.Surfaces.Dashboard;
        var renderer = new SolidRenderer(Colors.DarkSlateGray);
        surface.UseRenderer(renderer);
        var host = new CarSurfaceWindowHost(window, surface, NavigationTestServices.Runtime, CarContentStyle.Light);
        try
        {
            host.Attach();
            await Task.Delay(100);
            surface.ResetFrameStatistics();
            for (var i = 0; i < 120; i++)
            {
                renderer.Offset = i * 3;
                surface.Invalidate();
                await Task.Delay(16);
            }

            Report("ios custom drawing (draw time)", surface.FrameStatistics);
            Check.True(surface.FrameStatistics.FrameCount >= 60);

            var map = new MKMapView();
            surface.UseViewController(() => new UIViewController { View = map });
            await Task.Delay(200);
            surface.ResetFrameStatistics();
            var center = new CoreLocation.CLLocationCoordinate2D(52.2297, 21.0122);
            for (var i = 0; i < 8; i++)
            {
                map.SetRegion(new MKCoordinateRegion(new CoreLocation.CLLocationCoordinate2D(center.Latitude + (i * 0.002), center.Longitude), new MKCoordinateSpan(0.02, 0.02)), animated: true);
                await Task.Delay(250);
            }

            Report("ios native MKMapView (frame interval)", surface.FrameStatistics);
            Check.True(surface.FrameStatistics.FrameCount > 30);
        }
        finally
        {
            host.Detach();
            surface.ClearContent();
            window.Hidden = true;
        }
    }

    private static void Report(string mode, CarMapFrameStatistics stats) =>
        DeviceTestRunner.Info(FormattableString.Invariant(
            $"CARKIT-FRAMES {mode}: n={stats.FrameCount} avg={stats.Average.TotalMilliseconds:0.00} ms p50={stats.Median.TotalMilliseconds:0.00} ms p95={stats.Percentile95.TotalMilliseconds:0.00} ms max={stats.Maximum.TotalMilliseconds:0.00} ms >16.7ms={stats.SlowFrames} >33.3ms={stats.VerySlowFrames}"));
}
