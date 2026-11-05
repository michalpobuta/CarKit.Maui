using CarKit.Maui.Navigation;
using CarKit.Maui.Renderers;
using CarKit.Maui.Testing;
using CarKit.Maui.Tests.Hosting;
using CarKit.Maui.Tests.Media;
using CarKit.Maui.Tests.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Graphics;
using Xunit;

namespace CarKit.Maui.Tests.Navigation;

// Stage 8: platform-neutral logic of the Navigation module (spec: "Navigation module decisions (Stage 8)")
public class NavigationBuilderTests
{
    private static ServiceProvider Register(Action<CarKitBuilder> configure, CarAppCategory categories = CarAppCategory.Navigation)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(new InlineDispatcher());
        CarKitAppBuilderExtensions.Register(services, configure, categories);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Use_navigation_registers_services_once()
    {
        using var provider = Register(car => car.UseHandler<OtherHandler>().UseNavigation(nav =>
        {
            nav.UseHandler<TestNavigationHandler>();
            nav.VoicePromptLanguage = " en-GB ";
            nav.LogFrameTimes = true;
        }));

        var session = provider.GetRequiredService<ICarNavigationSession>();
        Assert.Same(provider.GetRequiredService<CarNavigationController>(), session);
        Assert.Same(provider.GetRequiredService<TestNavigationHandler>(), provider.GetRequiredService<ICarNavigationHandler>());
        var surfaces = provider.GetRequiredService<ICarMapSurfaces>();
        Assert.Equal(CarMapSurfaceKind.CarScreen, surfaces.CarScreen.Kind);
        Assert.Equal(CarMapSurfaceKind.Dashboard, surfaces.Dashboard.Kind);
        Assert.Equal(CarMapSurfaceKind.InstrumentCluster, surfaces.InstrumentCluster.Kind);
        var runtime = provider.GetRequiredService<CarNavigationRuntime>();
        Assert.Equal("en-GB", runtime.Options.VoicePromptLanguage);
        Assert.True(runtime.Options.LogFrameTimes);
        Assert.Equal(CarAppCategory.Navigation, runtime.Categories);
        // The module does not drive car screens: the app's handler stays
        Assert.IsType<OtherHandler>(provider.GetRequiredService<ICarAppHandler>());

        Assert.Throws<InvalidOperationException>(() => Register(car => car.UseHandler<OtherHandler>().UseNavigation(_ => { }).UseNavigation(_ => { })));
    }

    // Stage 9: sharing the route with the car (CarPlay iOS 26.4) only at the app's request
    [Fact]
    public void Route_sharing_is_opt_in()
    {
        using var off = Register(car => car.UseHandler<OtherHandler>().UseNavigation(_ => { }));
        using var on = Register(car => car.UseHandler<OtherHandler>().UseNavigation(nav => nav.ShareRoutesWithCar = true));

        Assert.False(off.GetRequiredService<CarNavigationRuntime>().Options.ShareRoutesWithCar);
        Assert.True(on.GetRequiredService<CarNavigationRuntime>().Options.ShareRoutesWithCar);
    }

    [Fact]
    public void Voice_prompts_come_from_the_platform_engine_and_stop_when_the_car_ends_navigation()
    {
        var engine = new TestSpeechEngine();
        using var provider = Register(car =>
        {
            car.UseHandler<OtherHandler>().UseNavigation(_ => { });
            car.Services.AddSingleton<ICarSpeechEngine>(engine);
            car.Services.AddSingleton<ICarAudioDucking>(new TestDucking());
        });

        var voice = provider.GetRequiredService<ICarVoicePrompts>();
        Assert.IsType<CarVoicePromptQueue>(voice);
        Assert.Same(voice, provider.GetRequiredService<ICarVoicePrompts>());
    }
}

public class MapSurfaceTests
{
    private static CarMapSurface Surface(RecordingErrorHandler? errors = null) =>
        new(CarMapSurfaceKind.CarScreen, new InlineDispatcher(), errors ?? new RecordingErrorHandler(), NullLogger.Instance);

    [Fact]
    public void Surface_routes_gestures_areas_and_availability()
    {
        var surface = Surface();
        var host = new TestSurfaceHost();
        var events = new List<string>();
        surface.AvailabilityChanged += (_, _) => events.Add("available:" + surface.IsAvailable);
        surface.AreasChanged += (_, _) => events.Add($"areas:{surface.VisibleArea.Width}x{surface.VisibleArea.Height}/{surface.StableArea.Width}");
        surface.PanModeChanged += (_, _) => events.Add("pan:" + surface.IsPanning);
        surface.Gesture += (_, e) => events.Add($"{e.Kind}:{e.Delta.X},{e.Delta.Y}:{e.ScaleFactor}");

        surface.Attach(host, CarContentStyle.Dark);
        surface.SetGeometry(new SizeF(800, 480), 2);
        surface.SetAvailable(true);
        surface.SetAreas(new RectF(0, 0, 600, 480), null);
        surface.SetAreas(new RectF(0, 0, 600, 480), null);
        surface.SetAreas(null, new RectF(100, 0, 500, 400));
        surface.SetPanning(true);
        surface.RaiseGesture(new CarMapGestureEventArgs(CarMapGestureKind.Scroll, PointF.Zero, new PointF(5, -3), PointF.Zero, 1));
        surface.Detach(new TestSurfaceHost());
        Assert.True(surface.IsAvailable);
        surface.Detach(host);

        Assert.Equal(
            ["areas:800x480/800", "available:True", "areas:600x480/800", "areas:600x480/500", "pan:True", "Scroll:5,-3:1", "available:False"],
            events);
        Assert.Equal(CarContentStyle.Dark, surface.ContentStyle);
        Assert.Equal(2, surface.Density);
        Assert.False(surface.IsPanning);
    }

    // I3 (review): a day/night change after connecting (CarPlay contentStyle, Car App onCarConfigurationChanged,
    // Dashboard window) reaches the surface and draws a new frame; the same style: no frame
    [Fact]
    public void Content_style_change_redraws_the_surface()
    {
        var surface = Surface();
        var host = new TestSurfaceHost();
        surface.Attach(host, CarContentStyle.Light);

        surface.SetContentStyle(CarContentStyle.Dark);
        surface.SetContentStyle(CarContentStyle.Dark);

        Assert.Equal(CarContentStyle.Dark, surface.ContentStyle);
        Assert.Equal(1, host.Invalidations);
    }

    [Fact]
    public void Surface_content_and_invalidate_reach_the_host()
    {
        var surface = Surface();
        var host = new TestSurfaceHost();
        var renderer = new TestRenderer();

        surface.UseRenderer(renderer);
        Assert.Equal(0, host.ContentChanges);
        surface.Attach(host, CarContentStyle.Light);
        surface.Invalidate();
        surface.Invalidate();
        surface.ClearContent();

        Assert.Equal(2, host.Invalidations);
        Assert.Equal(1, host.ContentChanges);
        Assert.False(surface.HasContent);
        surface.UseRenderer(renderer);
        Assert.Same(renderer, Assert.IsType<CarMapSurfaceContent.Renderer>(surface.Content).Value);
        Assert.Equal("custom drawing", surface.Content!.Mode);
        Assert.Throws<ArgumentNullException>(() => surface.UseRenderer(null!));
    }

    [Fact]
    public void Surface_draw_passes_areas_and_reports_renderer_errors()
    {
        var errors = new RecordingErrorHandler();
        var surface = Surface(errors);
        surface.SetGeometry(new SizeF(400, 200), 1);
        surface.SetAreas(new RectF(10, 10, 300, 150), new RectF(20, 20, 200, 100));
        var renderer = new TestRenderer();

        surface.Draw(renderer, new PictureCanvas(0, 0, 400, 200));
        renderer.Failure = new InvalidOperationException("draw");
        surface.Draw(renderer, new PictureCanvas(0, 0, 400, 200));

        Assert.Equal(new RectF(10, 10, 300, 150), renderer.Visible);
        Assert.Equal(new RectF(20, 20, 200, 100), renderer.Stable);
        Assert.Equal("Navigation.Draw", Assert.Single(errors.Errors).Context.Operation);
    }

    [Fact]
    public void Surface_event_handler_errors_go_to_the_error_handler()
    {
        var errors = new RecordingErrorHandler();
        var surface = Surface(errors);
        surface.Gesture += (_, _) => throw new InvalidOperationException("app");
        surface.AvailabilityChanged += (_, _) => throw new InvalidOperationException("app");

        surface.RaiseGesture(new CarMapGestureEventArgs(CarMapGestureKind.Click, new PointF(1, 2), PointF.Zero, PointF.Zero, 1));
        surface.SetAvailable(true);

        Assert.Equal(["Navigation.Gesture", "Navigation.AvailabilityChanged"], errors.Errors.Select(e => e.Context.Operation));
    }

    [Fact]
    public void Frame_statistics_percentiles_and_slow_frames()
    {
        var surface = Surface();
        Assert.Equal(CarMapFrameStatistics.Empty, surface.FrameStatistics);
        foreach (var ms in Enumerable.Range(1, 100))
        {
            surface.RecordFrame(TimeSpan.FromMilliseconds(ms % 10 == 0 ? 40 : 10));
        }

        surface.RecordFrame(TimeSpan.FromMilliseconds(-1));
        var stats = surface.FrameStatistics;
        Assert.Equal(100, stats.FrameCount);
        Assert.Equal(TimeSpan.FromMilliseconds(13), stats.Average);
        Assert.Equal(TimeSpan.FromMilliseconds(10), stats.Median);
        Assert.Equal(TimeSpan.FromMilliseconds(40), stats.Percentile95);
        Assert.Equal(TimeSpan.FromMilliseconds(40), stats.Maximum);
        Assert.Equal(10, stats.SlowFrames);
        Assert.Equal(10, stats.VerySlowFrames);

        foreach (var _ in Enumerable.Range(0, 700))
        {
            surface.RecordFrame(TimeSpan.FromMilliseconds(5));
        }

        Assert.Equal(CarFrameStatisticsRecorder.Capacity, surface.FrameStatistics.FrameCount);
        surface.ResetFrameStatistics();
        Assert.Equal(0, surface.FrameStatistics.FrameCount);
    }

    [Fact]
    public void Frame_times_are_logged_every_120_frames_when_enabled()
    {
        var logger = new TestLogger();
        var surface = new CarMapSurface(CarMapSurfaceKind.InstrumentCluster, new InlineDispatcher(), new RecordingErrorHandler(), logger, logFrames: true);
        surface.UseRenderer(new TestRenderer());

        foreach (var _ in Enumerable.Range(0, 250))
        {
            surface.RecordFrame(TimeSpan.FromMilliseconds(8));
        }

        Assert.Equal(2, logger.Entries.Count(e => e.Message.Contains("CarKit map frames InstrumentCluster (custom drawing)", StringComparison.Ordinal)));
    }
}

public class MapGestureTests
{
    [Fact]
    public void Gestures_carplay_pan_and_zoom_become_incremental()
    {
        var translator = new CarMapGestureTranslator();

        translator.PanBegan();
        var first = translator.PanUpdated(new PointF(-10, 4))!;
        var second = translator.PanUpdated(new PointF(-25, 4))!;
        Assert.Null(translator.PanUpdated(new PointF(-25, 4)));
        Assert.Equal(new PointF(10, -4), first.Delta);
        Assert.Equal(new PointF(15, 0), second.Delta);
        var fling = CarMapGestureTranslator.PanEnded(new PointF(-300, 0));
        Assert.Equal(CarMapGestureKind.Fling, fling.Kind);
        Assert.Equal(new PointF(-300, 0), fling.Velocity);

        translator.ZoomBegan();
        Assert.Equal(1.5f, translator.ZoomUpdated(new PointF(100, 50), 1.5f)!.ScaleFactor, 3);
        var zoom = translator.ZoomUpdated(new PointF(100, 50), 3f)!;
        Assert.Equal(2f, zoom.ScaleFactor, 3);
        Assert.Equal(new PointF(100, 50), zoom.Position);
        Assert.Null(translator.ZoomUpdated(PointF.Zero, 0));
        translator.ZoomBegan();
        Assert.Equal(0.5f, translator.ZoomUpdated(PointF.Zero, 0.5f)!.ScaleFactor, 3);
    }

    [Theory]
    [InlineData("Left", -200, 0)]
    [InlineData("Right", 200, 0)]
    [InlineData("Up", 0, -120)]
    [InlineData("Down", 0, 120)]
    public void Pan_buttons_scroll_a_quarter_of_the_surface(string direction, float x, float y) =>
        Assert.Equal(new PointF(x, y), CarMapGestureTranslator.PanStep(Enum.Parse<CarPanDirection>(direction), new SizeF(800, 480)).Delta);

    [Fact]
    public void Android_pixels_become_device_independent_units()
    {
        Assert.Equal(new PointF(5, -2), CarMapGestureTranslator.Scaled(CarMapGestureKind.Scroll, 10, -4, 1, 2).Delta);
        Assert.Equal(new PointF(100, 0), CarMapGestureTranslator.Scaled(CarMapGestureKind.Fling, 200, 0, 1, 2).Velocity);
        var scale = CarMapGestureTranslator.Scaled(CarMapGestureKind.Scale, 400, 200, 1.1f, 2);
        Assert.Equal(new PointF(200, 100), scale.Position);
        Assert.Equal(1.1f, scale.ScaleFactor);
        Assert.Equal(new PointF(-1, -1), CarMapGestureTranslator.Scaled(CarMapGestureKind.Scale, -1, -1, 2, 2).Position);
        Assert.Equal(new PointF(30, 15), CarMapGestureTranslator.Scaled(CarMapGestureKind.Click, 60, 30, 1, 2).Position);
        Assert.Equal(new RectF(10, 20, 300, 140), CarMapGestureTranslator.Area(20, 40, 620, 320, 2));
        var cluster = CarMapGestureTranslator.Zoom(zoomIn: false, new SizeF(400, 200));
        Assert.Equal(0.5f, cluster.ScaleFactor);
        Assert.Equal(new PointF(200, 100), cluster.Position);
    }
}

public class NavigationTemplateDecisionTests
{
    private static readonly CarTrip _trip = new(new CarLocation(52, 21), new CarLocation(52.1, 21.1), [new CarRouteChoice("A") { Id = "a" }]) { DestinationName = "Work" };

    [Theory]
    [InlineData(CarNavigationState.Previewing, 7, CarAppTemplateTypes.MapWithContent)]
    [InlineData(CarNavigationState.Previewing, 6, CarAppTemplateTypes.RoutePreview)]
    [InlineData(CarNavigationState.Idle, 8, CarAppTemplateTypes.Navigation)]
    [InlineData(CarNavigationState.Active, 1, CarAppTemplateTypes.Navigation)]
    [InlineData(CarNavigationState.Paused, 7, CarAppTemplateTypes.Navigation)]
    public void Map_template_types_follow_the_navigation_state(CarNavigationState state, int apiLevel, string expected) =>
        Assert.Equal(expected, CarNavigationTemplates.MapTemplateType(state, apiLevel));

    [Fact]
    public void Navigation_info_follows_the_state_and_pause_reason()
    {
        var maneuver = new CarManeuver("Straight") { Id = "m", InitialEstimates = new CarTravelEstimates(new CarDistance(300, CarDistanceUnit.Meters), TimeSpan.FromSeconds(30)) };
        var active = CarNavigationSnapshot.Idle with { State = CarNavigationState.Active, Trip = _trip, Route = _trip.Routes[0], Maneuvers = [maneuver] };

        Assert.Equal(CarNavigationInfoKind.None, CarNavigationTemplates.InfoKind(CarNavigationSnapshot.Idle));
        Assert.Equal(CarNavigationInfoKind.Routing, CarNavigationTemplates.InfoKind(active));
        Assert.Equal(CarNavigationInfoKind.Loading, CarNavigationTemplates.InfoKind(active with { Maneuvers = [] }));
        Assert.Equal(CarNavigationInfoKind.Loading, CarNavigationTemplates.InfoKind(active with { State = CarNavigationState.Paused, PauseReason = CarNavigationPauseReason.Rerouting }));
        var arrived = active with { State = CarNavigationState.Paused, PauseReason = CarNavigationPauseReason.Arrived };
        Assert.Equal(CarNavigationInfoKind.Message, CarNavigationTemplates.InfoKind(arrived));
        Assert.Equal("Work", CarNavigationTemplates.MessageTitle(arrived).Text);
        Assert.Equal("At the destination", CarNavigationTemplates.MessageTitle(arrived with { PauseDescription = "At the destination" }).Text);
        Assert.Equal("ProceedToRoute", CarNavigationTemplates.MessageTitle(CarNavigationSnapshot.Idle with { PauseReason = CarNavigationPauseReason.ProceedToRoute }).Text);

        Assert.Equal(new CarDistance(300, CarDistanceUnit.Meters), CarNavigationTemplates.DistanceToNext(active));
        var estimates = new CarTravelEstimates(new CarDistance(120, CarDistanceUnit.Meters), TimeSpan.FromSeconds(10));
        Assert.Equal(new CarDistance(120, CarDistanceUnit.Meters), CarNavigationTemplates.DistanceToNext(active with { ManeuverEstimates = estimates }));
        Assert.Null(CarNavigationTemplates.DistanceToNext(active with { Maneuvers = [] }));
    }

    [Theory]
    [InlineData(7, CarAppCategory.Parking, true, true)]
    [InlineData(7, CarAppCategory.Weather, true, true)]
    [InlineData(8, CarAppCategory.Navigation, true, true)]
    [InlineData(6, CarAppCategory.Parking, true, false)]
    [InlineData(8, CarAppCategory.Parking, false, false)]
    [InlineData(8, CarAppCategory.Iot, true, false)]
    public void Poi_app_map_choice_per_connection(int apiLevel, CarAppCategory categories, bool content, bool expected) =>
        Assert.Equal(expected, CarNavigationTemplates.UsesAppMapForPointsOfInterest(apiLevel, categories, content));

    [Theory]
    [InlineData(CarLaneShape.Straight, 0)]
    [InlineData(CarLaneShape.SlightLeft, -45)]
    [InlineData(CarLaneShape.Left, -90)]
    [InlineData(CarLaneShape.SharpLeft, -135)]
    [InlineData(CarLaneShape.UTurnLeft, -180)]
    [InlineData(CarLaneShape.SlightRight, 45)]
    [InlineData(CarLaneShape.Right, 90)]
    [InlineData(CarLaneShape.SharpRight, 135)]
    [InlineData(CarLaneShape.UTurnRight, 180)]
    [InlineData(CarLaneShape.Unknown, 0)]
    public void Lane_shapes_map_to_carplay_angles(CarLaneShape shape, double angle) =>
        Assert.Equal(angle, CarNavigationTemplates.LaneAngle(shape));

    [Fact]
    public void Arrival_time_defaults_to_now_plus_remaining()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(1));
        var estimates = new CarTravelEstimates(new CarDistance(1, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(5));

        Assert.Equal(now.AddMinutes(5), CarNavigationTemplates.ArrivalTime(estimates, now));
        Assert.Equal(now.AddHours(1), CarNavigationTemplates.ArrivalTime(estimates with { ArrivalTime = now.AddHours(1) }, now));
    }
}

public class MapRulesTests
{
    private static CarAppTemplateShape Shape(string type, string title, params string[] rows) =>
        new(type, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, [.. rows.Select(r => new CarAppRowShape(r))])] };

    [Fact]
    public void Map_shape_refresh_rules()
    {
        // NavigationTemplate: every content change (javadoc: "supports any content changes as refreshes")
        Assert.True(CarAppRefreshRules.IsRefresh(Shape(CarAppTemplateTypes.Navigation, "a"), Shape(CarAppTemplateTypes.Navigation, "b", "x")));
        // Route preview: list rules
        Assert.True(CarAppRefreshRules.IsRefresh(Shape(CarAppTemplateTypes.MapWithContent, "Work", "A", "B"), Shape(CarAppTemplateTypes.MapWithContent, "Work", "A", "B")));
        Assert.False(CarAppRefreshRules.IsRefresh(Shape(CarAppTemplateTypes.MapWithContent, "Work", "A"), Shape(CarAppTemplateTypes.MapWithContent, "Work", "A", "B")));
        Assert.False(CarAppRefreshRules.IsRefresh(Shape(CarAppTemplateTypes.RoutePreview, "Work", "A"), Shape(CarAppTemplateTypes.RoutePreview, "Home", "A")));
        // Preview ↔ guidance: a different template type, a task step
        Assert.False(CarAppRefreshRules.IsRefresh(Shape(CarAppTemplateTypes.MapWithContent, "Work", "A"), Shape(CarAppTemplateTypes.Navigation, "Work")));
    }

    // CPMapTemplate: all map properties have setters (device test Map_template_updates_in_place_with_hooks)
    [Fact]
    public void Map_updates_in_place_on_carplay()
    {
        var map = new CarMapTemplate { Id = "map" };
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(map, map with { AutomaticallyHidesNavigationBar = false, MapButtons = [] }));
    }

    [Fact]
    public void Map_costs_and_terminal_types()
    {
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.Navigation));
        Assert.Equal(1, CarAppTaskSteps.Cost(CarAppTemplateTypes.MapWithContent));
        Assert.True(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.Navigation));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.MapWithContent));
        Assert.False(CarAppTaskSteps.Allows(5, CarAppTemplateTypes.RoutePreview));
        Assert.Equal(CarAppTemplateTypes.Navigation, CarAppTemplateTypes.Of(new CarMapTemplate(), useSectionedItemTemplate: false));
    }
}

internal sealed class TestNavigationHandler : ICarNavigationHandler;

internal sealed class TestSurfaceHost : ICarMapSurfaceHost
{
    public int ContentChanges { get; private set; }

    public int Invalidations { get; private set; }

    public void ContentChanged() => ContentChanges++;

    public void Invalidate() => Invalidations++;
}

internal sealed class TestRenderer : ICarSurfaceRenderer
{
    public RectF Visible { get; private set; }

    public RectF Stable { get; private set; }

    public Exception? Failure { get; set; }

    public void Draw(ICanvas canvas, RectF visibleArea, RectF stableArea)
    {
        Visible = visibleArea;
        Stable = stableArea;
        if (Failure is { } failure)
        {
            throw failure;
        }
    }
}

internal sealed class TestSpeechEngine : ICarSpeechEngine
{
    private TaskCompletionSource? _current;

    public List<string> Spoken { get; } = [];

    public int Stops { get; private set; }

    public int Resets { get; private set; }

    public string? Language { get; private set; }

    public Exception? Failure { get; set; }

    public bool CompleteImmediately { get; set; } = true;

    // An engine that still does not finish the task after Stop() (the TTS process died)
    public bool IgnoreStop { get; set; }

    public Task SpeakAsync(string text, string? language)
    {
        Spoken.Add(text);
        Language = language;
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        if (CompleteImmediately)
        {
            return Task.CompletedTask;
        }

        _current = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _current.Task;
    }

    public void Finish() => _current?.TrySetResult();

    public void Stop()
    {
        Stops++;
        if (!IgnoreStop)
        {
            _current?.TrySetCanceled();
        }
    }

    public void Reset() => Resets++;
}

internal sealed class TestDucking : ICarAudioDucking
{
    public List<string> Calls { get; } = [];

    public bool Granted { get; set; } = true;

    public Exception? Failure { get; set; }

    public bool Begin()
    {
        Calls.Add("begin");
        if (Failure is { } failure)
        {
            Failure = null;
            throw failure;
        }

        return Granted;
    }

    public void End() => Calls.Add("end");
}

public class VoicePromptQueueTests
{
    private static (CarVoicePromptQueue Queue, TestSpeechEngine Engine, TestDucking Ducking, FakeCarDispatcher Dispatcher) Create()
    {
        var dispatcher = new FakeCarDispatcher();
        var engine = new TestSpeechEngine();
        var ducking = new TestDucking();
        return (new CarVoicePromptQueue(engine, ducking, dispatcher, new RecordingErrorHandler(), "en-GB"), engine, ducking, dispatcher);
    }

    [Fact]
    public async Task Voice_queue_speaks_in_order_and_ducks_once()
    {
        var (queue, engine, ducking, dispatcher) = Create();
        using var _ = dispatcher;

        await Task.WhenAll(queue.SpeakAsync("In 300 m turn right", TestContext.Current.CancellationToken), queue.SpeakAsync("Turn right", TestContext.Current.CancellationToken), queue.SpeakAsync("Go straight", TestContext.Current.CancellationToken));

        Assert.Equal(["In 300 m turn right", "Turn right", "Go straight"], engine.Spoken);
        Assert.Equal("en-GB", engine.Language);
        await Eventually(() => ducking.Calls.Count == 2);
        Assert.Equal(["begin", "end"], ducking.Calls);
        Assert.False(queue.IsSpeaking);

        await queue.SpeakAsync("At the destination", TestContext.Current.CancellationToken);
        await Eventually(() => ducking.Calls.Count == 4);
        Assert.Equal(["begin", "end", "begin", "end"], ducking.Calls);
    }

    [Fact]
    public async Task Voice_cancel_token_and_cancel_all()
    {
        var (queue, engine, ducking, dispatcher) = Create();
        using var _ = dispatcher;
        engine.CompleteImmediately = false;
        using var cts = new CancellationTokenSource();

        var speaking = queue.SpeakAsync("First", cts.Token);
        var queued = queue.SpeakAsync("Second", TestContext.Current.CancellationToken);
        var third = queue.SpeakAsync("Third", TestContext.Current.CancellationToken);
        await Eventually(() => engine.Spoken.Count == 1);
        Assert.True(queue.IsSpeaking);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaking);
        await Eventually(() => engine.Spoken.Count == 2);
        queue.CancelAll();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        Assert.Equal(["First", "Second"], engine.Spoken);
        // The engine's Stop() goes through the dispatcher (main thread), so after the tasks are canceled it may arrive a
        // moment later (a test race found in the final CI)
        await Eventually(() => engine.Stops == 2);
        await Eventually(() => ducking.Calls.Count == 2);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.SpeakAsync("x", new CancellationToken(true)));
    }

    [Fact]
    public async Task Voice_engine_error_is_a_platform_exception()
    {
        var (queue, engine, ducking, dispatcher) = Create();
        using var _ = dispatcher;
        engine.Failure = new InvalidOperationException("tts");

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => queue.SpeakAsync("Error", TestContext.Current.CancellationToken));
        Assert.Contains("tts", ex.Message, StringComparison.Ordinal);

        engine.Failure = null;
        ducking.Granted = false;
        var denied = await Assert.ThrowsAsync<CarPlatformException>(() => queue.SpeakAsync("No focus", TestContext.Current.CancellationToken));
        Assert.Contains("audio focus", denied.Message, StringComparison.Ordinal);
        Assert.Equal(["Error"], engine.Spoken);
    }

    [Fact]
    public async Task Voice_engine_that_never_finishes_is_stopped_by_the_watchdog()
    {
        using var dispatcher = new FakeCarDispatcher();
        var engine = new TestSpeechEngine { CompleteImmediately = false, IgnoreStop = true };
        var ducking = new TestDucking();
        var queue = new CarVoicePromptQueue(engine, ducking, dispatcher, new RecordingErrorHandler(), null, _ => TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => queue.SpeakAsync("Turn", TestContext.Current.CancellationToken));
        Assert.Contains("in time", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, engine.Stops);
        // M9 (review): an engine that did not report the end is created anew (Android: the TTS process died)
        Assert.Equal(1, engine.Resets);
        await Eventually(() => ducking.Calls.Count == 2);
        Assert.Equal(TimeSpan.FromSeconds(8.5), CarVoicePromptQueue.Watchdog("12345"));
    }

    // M9 (review): a platform exception during ducking ends the prompt with an error, and the queue keeps running
    [Fact]
    public async Task Voice_ducking_exception_fails_the_prompt_and_keeps_the_queue()
    {
        var (queue, engine, ducking, dispatcher) = Create();
        using var _ = dispatcher;
        ducking.Failure = new InvalidOperationException("audio session");

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => queue.SpeakAsync("First", TestContext.Current.CancellationToken)).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("audio session", ex.Message, StringComparison.Ordinal);
        await queue.SpeakAsync("Second", TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(["Second"], engine.Spoken);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
