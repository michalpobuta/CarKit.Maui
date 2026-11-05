using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using AndroidX.Car.App.Serialization;
using CarKit.Maui.Navigation;
using CarKit.Maui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using AndroidView = Android.Views.View;
using Color = Microsoft.Maui.Graphics.Color;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarText = AndroidX.Car.App.Model.CarText;
using PointF = Microsoft.Maui.Graphics.PointF;
using Rect = Android.Graphics.Rect;
using RectF = Microsoft.Maui.Graphics.RectF;

namespace CarKit.Maui.DeviceTests;

// Stage 8: the Navigation module on the Car App Library without a host (spec: "Navigation module decisions (Stage 8)"):
// CarKitSurfaceCallback on a real Surface from an ImageReader (three modes, pixels, gestures, areas),
// NavigationTemplate/MapWithContentTemplate/RoutePreviewNavigationTemplate from the real renderer, Trip, POI with an app
// map, cluster, intents, TTS with focus, frames
internal static class NavigationTests
{
    private static readonly Handler _main = new(Looper.MainLooper!);

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Surface_callback_custom_drawing_pixels), Surface_callback_custom_drawing_pixels),
        new(nameof(Surface_callback_virtual_display_presentation_pixels), Surface_callback_virtual_display_presentation_pixels),
        new(nameof(Surface_callback_maui_view_pixels), Surface_callback_maui_view_pixels),
        new(nameof(Surface_callback_gestures_and_areas), Surface_callback_gestures_and_areas),
        new(nameof(Navigation_template_routing_info), Navigation_template_routing_info),
        new(nameof(Navigation_template_pause_and_message), Navigation_template_pause_and_message),
        new(nameof(Map_with_content_route_previews), Map_with_content_route_previews),
        new(nameof(Route_preview_below_api_7), Route_preview_below_api_7),
        new(nameof(Trip_for_navigation_manager), Trip_for_navigation_manager),
        new(nameof(Poi_with_app_map), Poi_with_app_map),
        new(nameof(Cluster_template), Cluster_template),
        new(nameof(Navigate_intent_reaches_the_handler), Navigate_intent_reaches_the_handler),
        new(nameof(Voice_prompt_takes_transient_focus), Voice_prompt_takes_transient_focus),
        new(nameof(Frame_times_custom_and_virtual_display), Frame_times_custom_and_virtual_display),
    ];

    private static CarAppNavigationModule Module => NavigationTestServices.Services.GetRequiredService<CarAppNavigationModule>();

    private sealed class Fixture
    {
        public Fixture(int apiLevel = 8, CarAppCategory categories = CarAppCategory.Navigation)
        {
            Test = TestSession.AndroidAuto(apiLevel, categories);
            var app = Android.App.Application.Context;
            Context = new CarAppRenderContext(
                Test.Session, Test, apiLevel, app, new CarImageLoader<CarIcon>(new CarAppImages(app), Test, combineDarkVariants: false),
                () => CarContentStyle.Light, work => _main.Post(work), useSectionedItemTemplate: false, Test,
                [.. NavigationTestServices.Services.GetServices<ICarAppModuleRenderer>()], usePlaceListMap: true);
            Screen = new CarAppScreenState { IsRoot = true, Rerender = () => Rerenders++ };
        }

        public TestSession Test { get; }

        public CarAppRenderContext Context { get; }

        public CarAppScreenState Screen { get; }

        public int Rerenders { get; set; }

        public CarAppRendered Render(CarTemplate template) => CarAppTemplateRenderers.Render(template, Context, Screen);
    }

    private sealed class Done : Java.Lang.Object, IOnDoneCallback
    {
        public void OnFailure(Bundleable? response)
        {
        }

        public void OnSuccess(Bundleable? response)
        {
        }
    }

    // A surface from an ImageReader (RGBA_8888) like the host's Surface: 400×240 px at 320 dpi (density 2)
    private sealed class SurfaceFixture : IDisposable
    {
        public SurfaceFixture(int width = 400, int height = 240)
        {
            Reader = ImageReader.NewInstance(width, height, (ImageFormatType)1, 3);
            Surface = new CarMapSurface(CarMapSurfaceKind.CarScreen, NavigationTestServices.Runtime.Dispatcher, NavigationTestServices.Runtime.Errors, NavigationTestServices.Runtime.Logger);
            Host = new CarSurfaceHost(Surface, NavigationTestServices.Runtime, Android.App.Application.Context, CarContentStyle.Light);
            Callback = new CarKitSurfaceCallback(Host);
            Container = new SurfaceContainer(Reader.Surface!, width, height, 320);
        }

        public ImageReader Reader { get; }

        public CarMapSurface Surface { get; }

        public CarSurfaceHost Host { get; }

        public CarKitSurfaceCallback Callback { get; }

        public SurfaceContainer Container { get; }

        // The last frame sent to the Surface, the color of pixel (x, y)
        public async Task<(int R, int G, int B)> PixelAsync(int x, int y, Func<(int R, int G, int B), bool> until)
        {
            (int R, int G, int B) last = default;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(30);
                using var image = Reader.AcquireLatestImage();
                if (image is null)
                {
                    continue;
                }

                var plane = image.GetPlanes()![0];
                var buffer = plane.Buffer!;
                var offset = (y * plane.RowStride) + (x * plane.PixelStride);
                last = (buffer.Get(offset) & 0xFF, buffer.Get(offset + 1) & 0xFF, buffer.Get(offset + 2) & 0xFF);
                if (until(last))
                {
                    return last;
                }
            }

            return last;
        }

        public void Dispose()
        {
            Callback.OnSurfaceDestroyed(Container);
            Reader.Close();
            Reader.Dispose();
        }
    }

    private static async Task Surface_callback_custom_drawing_pixels()
    {
        using var f = new SurfaceFixture();
        var renderer = new SolidRenderer(Colors.Red);
        f.Surface.UseRenderer(renderer);

        f.Callback.OnSurfaceAvailable(f.Container);
        var (r, g, b) = await f.PixelAsync(200, 200, p => p.R > 200);

        Check.True(r > 200 && g < 60 && b < 60, $"pixel {r},{g},{b} is red");
        Check.True(f.Surface.IsAvailable);
        Check.Equal(new SizeF(200, 120), f.Surface.Size);
        Check.Equal(2f, f.Surface.Density);
        Check.True(renderer.Frames > 0);
        Check.True(f.Surface.FrameStatistics.FrameCount > 0);
        Check.Null(f.Host.Display);
    }

    private static async Task Surface_callback_virtual_display_presentation_pixels()
    {
        using var f = new SurfaceFixture();
        f.Surface.UseView(context =>
        {
            var view = new AndroidView(context);
            view.SetBackgroundColor(Android.Graphics.Color.Blue);
            return view;
        });

        f.Callback.OnSurfaceAvailable(f.Container);
        var (r, g, b) = await f.PixelAsync(100, 100, p => p.B > 200);

        Check.True(b > 200 && r < 60 && g < 60, $"pixel {r},{g},{b} is blue");
        var display = Check.NotNull(f.Host.Display);
        Check.Equal(400, display.Display!.GetMode()!.PhysicalWidth);
        Check.NotNull(f.Host.Presentation);
        f.Callback.OnSurfaceDestroyed(f.Container);
        Check.Null(f.Host.Presentation);
        Check.True(!f.Surface.IsAvailable);
    }

    private static async Task Surface_callback_maui_view_pixels()
    {
        using var f = new SurfaceFixture();
#pragma warning disable CARKIT0002 // experimental mode, the test checks it on purpose
        f.Surface.UseMauiView(() => new Microsoft.Maui.Controls.BoxView { Color = Colors.Lime });
#pragma warning restore CARKIT0002

        f.Callback.OnSurfaceAvailable(f.Container);
        var (r, g, b) = await f.PixelAsync(100, 100, p => p.G > 200);

        Check.True(g > 200 && r < 60 && b < 60, $"pixel {r},{g},{b} is green");
        Check.Equal(0, NavigationErrors().Count);
    }

    private static List<string> NavigationErrors() => [.. DeviceTestErrors.Instance.Errors.Where(e => e.StartsWith("Navigation.", StringComparison.Ordinal))];

    private static async Task Surface_callback_gestures_and_areas()
    {
        using var f = new SurfaceFixture();
        var renderer = new SolidRenderer(Colors.Red);
        f.Surface.UseRenderer(renderer);
        var gestures = new List<CarMapGestureEventArgs>();
        f.Surface.Gesture += (_, e) => gestures.Add(e);
        f.Callback.OnSurfaceAvailable(f.Container);

        f.Callback.OnVisibleAreaChanged(new Rect(20, 40, 380, 240));
        f.Callback.OnStableAreaChanged(new Rect(40, 40, 360, 200));
        f.Callback.OnScroll(10, -4);
        f.Callback.OnScale(200, 100, 1.5f);
        f.Callback.OnFling(-600, 0);
        f.Callback.OnClick(60, 30);
        await Check.Eventually(() => renderer.Visible.Width == 180, "a frame with the new areas");

        Check.Equal(new RectF(10, 20, 180, 100), f.Surface.VisibleArea);
        Check.Equal(new RectF(20, 20, 160, 80), f.Surface.StableArea);
        Check.Equal(new RectF(20, 20, 160, 80), renderer.Stable);
        Check.Equal(4, gestures.Count);
        Check.Equal(new PointF(5, -2), gestures[0].Delta);
        Check.Equal(CarMapGestureKind.Scale, gestures[1].Kind);
        Check.Equal(new PointF(100, 50), gestures[1].Position);
        Check.Equal(1.5f, gestures[1].ScaleFactor);
        Check.Equal(new PointF(-300, 0), gestures[2].Velocity);
        Check.Equal(new PointF(30, 15), gestures[3].Position);
    }

    private static string? Text(NativeCarText? text) => text?.ToCharSequence();

    private static async Task Navigation_template_routing_info()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        var map = NavigationTestServices.Map();

        Check.Equal(CarAppTemplateTypes.Navigation, f.Context.TemplateType(map));
        var rendered = f.Render(map);
        var template = Check.Is<NavigationTemplate>(rendered.Template);
        Check.Equal(CarAppTemplateTypes.Navigation, rendered.Shape.Type);
        var routing = Check.NotNull(template.NavigationInfo).JavaCast<RoutingInfo>()!;
        Check.True(!routing.IsLoading);
        var current = Check.NotNull(routing.CurrentStep);
        Check.Equal("Turn right onto Main Street", Text(current.Cue));
        Check.Equal("Main Street", Text(current.Road));
        Check.Equal(AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnNormalRight, current.Maneuver!.Type);
        Check.NotNull(current.Maneuver.Icon);
        Check.Equal(2, current.Lanes!.Count);
        Check.True(current.Lanes[1].Directions![1].IsRecommended);
        Check.Equal(LaneDirection.ShapeNormalRight, current.Lanes[1].Directions![1].Shape);
        Check.NotNull(current.LanesImage);
        Check.Equal(300d, routing.CurrentDistance!.DisplayDistance);
        var next = Check.NotNull(routing.NextStep);
        Check.Equal(AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterAndExitCcw, next.Maneuver!.Type);
        Check.Equal(3, next.Maneuver.RoundaboutExitNumber);
        Check.Equal(12.4, template.DestinationTravelEstimate!.RemainingDistance!.DisplayDistance);
        Check.Equal(25 * 60L, template.DestinationTravelEstimate.RemainingTimeSeconds);
        Check.Equal(1, template.ActionStrip!.Actions!.Count);
        Check.Equal("Destination", Text(template.ActionStrip.Actions[0].Title));
        var mapActions = template.MapActionStrip!.Actions!;
        Check.Equal(3, mapActions.Count);
        Check.Equal(NativeAction.TypePan, mapActions[0].Type);
        Check.NotNull(template.PanModeDelegate);
        Check.NotNull(template.BackgroundColor);

        // Pan via the host path → CarScreen.IsPanning
        template.PanModeDelegate!.SendPanModeChanged(true, new Done());
        await Check.Eventually(() => NavigationTestServices.Runtime.Surfaces.CarScreen.IsPanning, "pan mode");
        template.PanModeDelegate.SendPanModeChanged(false, new Done());

        // A maneuver change rebuilds the screen (a NavigationTemplate refresh)
        Module.Connection(f.Context).RenderMap(map, f.Screen);
        await NavigationTestServices.Navigation.UpdateManeuversAsync([trip.Routes[0].Maneuvers[1]]);
        await Check.Eventually(() => f.Rerenders > 0, "a rerender after the maneuver update");
        Check.True(CarAppRefreshRules.IsRefresh(rendered.Shape, f.Render(map).Shape));
        Check.Equal(0, f.Test.Errors.Count);
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Navigation_template_pause_and_message()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();
        var trip = NavigationTestServices.Trip();
        var map = NavigationTestServices.Map();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);

        await NavigationTestServices.Navigation.PauseAsync(CarNavigationPauseReason.Rerouting);
        var loading = Check.Is<NavigationTemplate>(f.Render(map).Template).NavigationInfo!.JavaCast<RoutingInfo>()!;
        Check.True(loading.IsLoading);

        await NavigationTestServices.Navigation.PauseAsync(CarNavigationPauseReason.Arrived, "You have arrived");
        var message = Check.Is<NavigationTemplate>(f.Render(map).Template).NavigationInfo!.JavaCast<MessageInfo>()!;
        Check.Equal("You have arrived", Text(message.Title));

        await NavigationTestServices.Navigation.FinishAsync();
        Check.Null(Check.Is<NavigationTemplate>(f.Render(map).Template).NavigationInfo);
    }

    private static async Task Map_with_content_route_previews()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();
        var trip = NavigationTestServices.Trip();
        var map = NavigationTestServices.Map();
        await NavigationTestServices.Navigation.ShowTripPreviewsAsync([trip], trip);

        Check.Equal(CarAppTemplateTypes.MapWithContent, f.Context.TemplateType(map));
        var rendered = f.Render(map);
        var template = Check.Is<MapWithContentTemplate>(rendered.Template);
        var content = Check.Is<ListTemplate>(template.ContentTemplate);
        var list = Check.NotNull(content.SingleList);
        Check.Equal(2, list.Items!.Count);
        Check.Equal(0, list.SelectedIndex);
        var row = list.Items[0].JavaCast<Row>()!;
        Check.Equal("Via Riverside Drive", Text(row.Title));
        Check.Equal("Work", Text(content.Header!.Title));
        Check.NotNull(template.MapController?.MapActionStrip);
        Check.Equal(CarAppTemplateTypes.MapWithContent, rendered.Shape.Type);
#pragma warning disable CS0618 // ListTemplate.ActionStrip is obsolete (the renderer uses it for actions with a title)
        var start = content.ActionStrip!.Actions![0];
#pragma warning restore CS0618
        Check.Equal("Go", Text(start.Title));

        // Route selection and Start via the host path → the app's handler
        list.OnSelectedDelegate!.SendSelected(1, new Done());
        await Check.Eventually(() => NavigationTestServices.Handler.Calls.Contains("preview:work/eco"), "the preview handler");
        Check.Equal("eco", NavigationTestServices.Navigation.Snapshot.SelectedRoute?.Id);
        Check.Equal(1, Check.Is<ListTemplate>(Check.Is<MapWithContentTemplate>(f.Render(map).Template).ContentTemplate).SingleList!.SelectedIndex);
        start.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => NavigationTestServices.Handler.Calls.Contains("start:work/eco"), "the start handler");

        // Preview → guidance: a different template type (a task step)
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[1]);
        Check.True(!CarAppRefreshRules.IsRefresh(rendered.Shape, f.Render(map).Shape));
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Route_preview_below_api_7()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture(apiLevel: 6);
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.ShowTripPreviewsAsync([trip], null);

        var map = NavigationTestServices.Map();
        Check.Equal(CarAppTemplateTypes.RoutePreview, f.Context.TemplateType(map));
#pragma warning disable CS0618 // RoutePreviewNavigationTemplate is obsolete; the path of hosts < 7
        var template = Check.Is<RoutePreviewNavigationTemplate>(f.Render(map).Template);
        Check.Equal("Go", Text(template.NavigateAction!.Title));
        Check.Equal(2, template.ItemList!.Items!.Count);
        Check.Equal("Work", Text(template.Title));
#pragma warning restore CS0618
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Trip_for_navigation_manager()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);
        var icons = CarAppIcons.For(f.Context, f.Screen);

        var native = CarAppNavigationModels.Trip(NavigationTestServices.Navigation.Snapshot, icons, [], DateTimeOffset.Now);
        Check.Equal(1, native.Destinations!.Count);
        Check.Equal("Work", Text(native.Destinations[0].Name));
        Check.Equal("39 Oak Street", Text(native.Destinations[0].Address));
        Check.Equal(2, native.Steps!.Count);
        Check.Equal(2, native.StepTravelEstimates!.Count);
        Check.Equal(300d, native.StepTravelEstimates[0].RemainingDistance!.DisplayDistance);
        Check.True(!native.IsLoading);

        await NavigationTestServices.Navigation.PauseAsync(CarNavigationPauseReason.Rerouting);
        Check.True(CarAppNavigationModels.Trip(NavigationTestServices.Navigation.Snapshot, icons, [], DateTimeOffset.Now).IsLoading);
        await NavigationTestServices.ResetAsync();
    }

    private static Task Poi_with_app_map()
    {
        var surface = NavigationTestServices.Runtime.Surfaces.CarScreen;
        var points = new CarPointOfInterestTemplate("Parking lots")
        {
            Id = "poi",
            Points =
            [
                new CarPointOfInterest(new CarLocation(52.2297, 21.0122), "Downtown") { Id = "a", Subtitle = "10 Elm Street", Distance = new CarDistance(1.2, CarDistanceUnit.Kilometers) },
            ],
        };
        surface.UseRenderer(new SolidRenderer(Colors.Gray));
        try
        {
            var withMap = new Fixture(apiLevel: 7, categories: CarAppCategory.Parking);
            Check.Equal(CarAppTemplateTypes.MapWithContent, withMap.Context.TemplateType(points));
            var template = Check.Is<MapWithContentTemplate>(withMap.Render(points).Template);
            var list = Check.Is<ListTemplate>(template.ContentTemplate);
            var row = list.SingleList!.Items![0].JavaCast<Row>()!;
            Check.Equal("Downtown", Text(row.Title));
            Check.NotNull(row.Metadata?.Place);
            Check.NotNull(template.MapController);

            // Below API 7 the Stage 7 choice stays (PlaceListMapTemplate with the host's map)
            var older = new Fixture(apiLevel: 6, categories: CarAppCategory.Parking);
            Check.Equal(CarAppTemplateTypes.PlaceListMap, older.Context.TemplateType(points));
            Check.Is<PlaceListMapTemplate>(older.Render(points).Template);
        }
        finally
        {
            surface.ClearContent();
        }

        // Without CarScreen content at connection time: also the Stage 7 choice
        var withoutContent = new Fixture(apiLevel: 8, categories: CarAppCategory.Parking);
        Check.Is<PlaceListMapTemplate>(withoutContent.Render(points).Template);
        return Task.CompletedTask;
    }

    private static async Task Cluster_template()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();
        var trip = NavigationTestServices.Trip();
        await NavigationTestServices.Navigation.StartTripAsync(trip, trip.Routes[0]);

        var template = Check.Is<NavigationTemplate>(ClusterScreen.Template(NavigationTestServices.Navigation.Snapshot, CarAppIcons.For(f.Context, f.Screen), []));

        Check.Equal(NativeAction.TypeAppIcon, template.ActionStrip!.Actions![0].Type);
        Check.Null(template.MapActionStrip);
        Check.Equal("Turn right onto Main Street", Text(template.NavigationInfo!.JavaCast<RoutingInfo>()!.CurrentStep!.Cue));
        await NavigationTestServices.ResetAsync();
    }

    private static async Task Navigate_intent_reaches_the_handler()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture();

        Module.OnIntent(f.Context, new Intent(CarContext.ActionNavigate, Android.Net.Uri.Parse("geo:0,0?q=Main+Square+1")));
        Module.OnIntent(f.Context, new Intent(Intent.ActionView, Android.Net.Uri.Parse("geo:0,0?q=Other")));

        await Check.Eventually(() => NavigationTestServices.Handler.Calls.Contains("request:Main Square 1"), "the navigation request handler");
        Check.Equal(1, NavigationTestServices.Handler.Calls.Count);
    }

    // A competitor (like another app's player) with setWillPauseWhenDucked gets CAN_DUCK for the duration of the prompt and
    // GAIN after it
    private sealed class Competitor : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public List<AudioFocus> Changes { get; } = [];

        public void OnAudioFocusChange(AudioFocus focusChange) => Changes.Add(focusChange);
    }

    private static async Task Voice_prompt_takes_transient_focus()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var audio = (AudioManager)Android.App.Application.Context.GetSystemService(Context.AudioService)!;
        var competitor = new Competitor();
        var request = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!)!
            .SetWillPauseWhenDucked(true)!
            .SetOnAudioFocusChangeListener(competitor, _main)!
            .Build()!;
        Check.Equal(AudioFocusRequest.Granted, audio.RequestAudioFocus(request));
        var voice = NavigationTestServices.Services.GetRequiredService<ICarVoicePrompts>();
        var ducking = (CarAudioDucking)NavigationTestServices.Services.GetRequiredService<ICarAudioDucking>();
        try
        {
            var speaking = voice.SpeakAsync("Turn right");
            await Check.Eventually(() => ducking.Held, "the transient focus", 10000);
            await Check.Eventually(() => competitor.Changes.Contains(AudioFocus.LossTransientCanDuck), "the competitor to duck", 5000);
            try
            {
                await speaking.WaitAsync(TimeSpan.FromSeconds(15));
                DeviceTestRunner.Info("voice prompt spoken by TextToSpeech");
            }
            catch (CarPlatformException ex)
            {
                // An emulator without voice data: the focus and the queue work anyway; the result goes to the ledger
                DeviceTestRunner.Info("voice prompt: " + ex.Message);
            }

            await Check.Eventually(() => !ducking.Held, "the focus release", 5000);
            await Check.Eventually(() => competitor.Changes.Contains(AudioFocus.Gain), "the competitor to regain focus", 5000);
        }
        finally
        {
            audio.AbandonAudioFocusRequest(request);
        }
    }

    // Smoothness measurement (spec: Stage 8 acceptance): custom drawing of 120 frames and an animated view in a
    // Presentation on a VirtualDisplay
    private static async Task Frame_times_custom_and_virtual_display()
    {
        using var f = new SurfaceFixture(800, 480);
        var renderer = new SolidRenderer(Colors.DarkSlateGray);
        f.Surface.UseRenderer(renderer);
        f.Callback.OnSurfaceAvailable(f.Container);
        f.Reader.SetOnImageAvailableListener(new Drain(), _main);
        await Task.Delay(100);
        f.Surface.ResetFrameStatistics();
        for (var i = 0; i < 120; i++)
        {
            renderer.Offset = i * 3;
            f.Surface.Invalidate();
            await Task.Delay(16);
        }

        Report("android custom drawing (draw time)", f.Surface.FrameStatistics);
        Check.True(f.Surface.FrameStatistics.FrameCount >= 60);

        ValueAnimator? animator = null;
        f.Surface.UseView(context =>
        {
            var root = new Android.Widget.FrameLayout(context);
            root.SetBackgroundColor(Android.Graphics.Color.DarkGreen);
            var dot = new AndroidView(context);
            dot.SetBackgroundColor(Android.Graphics.Color.White);
            root.AddView(dot, new Android.Widget.FrameLayout.LayoutParams(40, 40));
            animator = ValueAnimator.OfFloat(0, 600)!;
            animator.SetDuration(1000);
            animator.RepeatCount = ValueAnimator.Infinite;
            animator.Update += (_, e) => dot.TranslationX = (float)e.Animation.AnimatedValue!;
            animator.Start();
            return root;
        });
        await Task.Delay(300);
        f.Surface.ResetFrameStatistics();
        await Task.Delay(2000);
        animator?.Cancel();
        Report("android VirtualDisplay + Presentation (FrameMetrics total)", f.Surface.FrameStatistics);
        Check.True(f.Surface.FrameStatistics.FrameCount > 30);
    }

    // Releases the ImageReader frames so the producer does not wait for a free buffer
    private sealed class Drain : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader) => reader?.AcquireLatestImage()?.Close();
    }

    private static void Report(string mode, CarMapFrameStatistics stats) =>
        DeviceTestRunner.Info(FormattableString.Invariant(
            $"CARKIT-FRAMES {mode}: n={stats.FrameCount} avg={stats.Average.TotalMilliseconds:0.00} ms p50={stats.Median.TotalMilliseconds:0.00} ms p95={stats.Percentile95.TotalMilliseconds:0.00} ms max={stats.Maximum.TotalMilliseconds:0.00} ms >16.7ms={stats.SlowFrames} >33.3ms={stats.VerySlowFrames}"));
}
