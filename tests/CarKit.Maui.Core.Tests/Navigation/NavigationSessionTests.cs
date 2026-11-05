using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Navigation;

// Stage 8: navigation session state machine (spec: "Navigation module decisions (Stage 8)", Navigation session)
internal sealed class RecordingSink(FakeCarDispatcher dispatcher) : ICarNavigationSink
{
    public List<string> Calls { get; } = [];

    public List<CarNavigationSnapshot> States { get; } = [];

    public List<CarNavigationChange> Changes { get; } = [];

    public Exception? Failure { get; set; }

    public bool AllOnMainThread { get; private set; } = true;

    public void Apply(CarNavigationChange change, CarNavigationSnapshot state)
    {
        AllOnMainThread &= !dispatcher.IsDispatchRequired;
        Calls.Add(change.GetType().Name);
        Changes.Add(change);
        States.Add(state);
        if (Failure is { } failure)
        {
            throw failure;
        }
    }

    public void Restore(CarNavigationSnapshot state)
    {
        AllOnMainThread &= !dispatcher.IsDispatchRequired;
        Calls.Add("Restore:" + state.State);
        States.Add(state);
    }
}

internal sealed class RecordingNavigationHandler : ICarNavigationHandler
{
    public List<string> Calls { get; } = [];

    public Func<Task>? OnStart { get; set; }

    public bool OnMainThread { get; private set; } = true;

    public FakeCarDispatcher? Dispatcher { get; set; }

    public Task OnTripPreviewSelectedAsync(CarTrip trip, CarRouteChoice route) => Record($"preview:{trip.Id}/{route.Id}");

    public async Task OnTripStartRequestedAsync(CarTrip trip, CarRouteChoice route)
    {
        await Record($"start:{trip.Id}/{route.Id}");
        if (OnStart is { } start)
        {
            await start();
        }
    }

    public Task OnNavigationEndedByCarAsync() => Record("ended");

    public Task OnManeuverSelectedAsync(CarManeuver maneuver) => Record("maneuver:" + maneuver.Id);

    public Task OnNavigationAlertSelectedAsync(CarNavigationAlert alert) => Record("alert:" + alert.Id);

    public Task OnNavigationRequestedAsync(CarNavigationRequest request) => Record("request:" + (request.Query ?? request.Location?.ToString()));

    public Task OnAutoDriveRequestedAsync() => Record("autodrive");

    private Task Record(string call)
    {
        OnMainThread &= Dispatcher?.IsDispatchRequired != true;
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

internal sealed class NavigationFixture : IDisposable
{
    public NavigationFixture(ICarNavigationHandler? handler = null)
    {
        Handler = handler as RecordingNavigationHandler ?? new RecordingNavigationHandler();
        Handler.Dispatcher = Dispatcher;
        Controller = new CarNavigationController(Dispatcher, Errors, () => handler ?? Handler, Logger);
        Sink = new RecordingSink(Dispatcher);
    }

    public FakeCarDispatcher Dispatcher { get; } = new();

    public TestErrors Errors { get; } = new();

    public TestLogger Logger { get; } = new();

    public RecordingNavigationHandler Handler { get; }

    public CarNavigationController Controller { get; }

    public RecordingSink Sink { get; }

    public async Task<NavigationFixture> AttachedAsync()
    {
        await Controller.AttachAsync(Sink);
        return this;
    }

    public void Dispose() => Dispatcher.Dispose();
}

internal sealed class TestErrors : ICarErrorHandler
{
    public List<(Exception Exception, string Operation)> Errors { get; } = [];

    public void HandleError(Exception exception, CarErrorContext context) => Errors.Add((exception, context.Operation));
}

public class NavigationSessionTests
{
    [Fact]
    public async Task Navigation_previews_start_maneuvers_and_finish()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        var states = new List<CarNavigationState>();
        nav.StateChanged += (_, state) => states.Add(state);

        await nav.ShowTripPreviewsAsync([trip, N.Trip("other")], trip);
        Assert.Equal(CarNavigationState.Previewing, nav.State);
        Assert.Equal(2, nav.TripPreviews.Count);
        Assert.Same(trip, f.Sink.States[^1].SelectedPreview);

        await nav.StartTripAsync(trip, trip.Routes[0]);
        Assert.Equal(CarNavigationState.Active, nav.State);
        Assert.Same(trip, nav.Trip);
        Assert.Same(trip.Routes[0], nav.Route);
        Assert.Equal(["m1", "m2"], nav.UpcomingManeuvers.Select(m => m.Id));
        Assert.Empty(nav.TripPreviews);

        await nav.UpdateManeuversAsync([N.Maneuver("m2")]);
        await nav.UpdateEstimatesAsync(N.Estimates(5, 9), N.Estimates(0.2, 1));
        Assert.Equal(N.Estimates(5, 9), f.Sink.States[^1].TripEstimates);
        Assert.Equal(N.Estimates(0.2, 1), f.Sink.States[^1].ManeuverEstimates);
        await nav.FinishAsync();

        Assert.Equal(CarNavigationState.Idle, nav.State);
        Assert.Null(nav.Trip);
        Assert.Empty(nav.UpcomingManeuvers);
        Assert.Equal(["Restore:Idle", "PreviewsShown", "TripStarted", "ManeuversUpdated", "EstimatesUpdated", "Finished"], f.Sink.Calls);
        Assert.Equal([CarNavigationState.Previewing, CarNavigationState.Active, CarNavigationState.Idle], states);
        Assert.True(f.Sink.AllOnMainThread);
    }

    public static TheoryData<string, string> InvalidTransitions => new()
    {
        { "Idle", "UpdateManeuvers" },
        { "Idle", "UpdateEstimates" },
        { "Idle", "Reroute" },
        { "Idle", "Pause" },
        { "Idle", "Finish" },
        { "Idle", "Cancel" },
        { "Previewing", "UpdateManeuvers" },
        { "Previewing", "Pause" },
        { "Previewing", "Finish" },
        { "Active", "ShowTripPreviews" },
        { "Active", "StartTrip" },
        { "Paused", "ShowTripPreviews" },
    };

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public async Task Navigation_rejects_transitions_outside_the_table(string state, string operation)
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        switch (state)
        {
            case "Previewing":
                await nav.ShowTripPreviewsAsync([trip], null);
                break;
            case "Active":
                await nav.StartTripAsync(trip, trip.Routes[0]);
                break;
            case "Paused":
                await nav.StartTripAsync(trip, trip.Routes[0]);
                await nav.PauseAsync(CarNavigationPauseReason.Locating);
                break;
        }

        var before = nav.State;
        var calls = f.Sink.Calls.Count;
        Task Run() => operation switch
        {
            "UpdateManeuvers" => nav.UpdateManeuversAsync([N.Maneuver("x")]),
            "UpdateEstimates" => nav.UpdateEstimatesAsync(N.Estimates(1, 1), null),
            "Reroute" => nav.RerouteAsync(N.Route("new")),
            "Pause" => nav.PauseAsync(CarNavigationPauseReason.Rerouting),
            "Finish" => nav.FinishAsync(),
            "Cancel" => nav.CancelAsync(),
            "ShowTripPreviews" => nav.ShowTripPreviewsAsync([trip], null),
            "StartTrip" => nav.StartTripAsync(trip, trip.Routes[0]),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Run);
        Assert.Contains(before.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, nav.State);
        Assert.Equal(calls, f.Sink.Calls.Count);
    }

    [Fact]
    public async Task Navigation_rejects_bad_arguments()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();

        await Assert.ThrowsAsync<ArgumentException>(() => nav.ShowTripPreviewsAsync([trip], N.Trip("not-shown")));
        await Assert.ThrowsAsync<ArgumentException>(() => nav.ShowTripPreviewsAsync([trip, N.Trip("trip")], null));
        await Assert.ThrowsAsync<ArgumentException>(() => nav.StartTripAsync(trip, N.Route("foreign")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => nav.StartTripAsync(trip, null!));
        await nav.StartTripAsync(trip, trip.Routes[0]);
        await Assert.ThrowsAsync<ArgumentException>(() => nav.UpdateManeuversAsync([N.Maneuver("r", CarManeuverKind.Roundabout)]));
        await Assert.ThrowsAsync<ArgumentException>(() => nav.UpdateManeuversAsync([N.Maneuver("a"), N.Maneuver("a")]));
        await nav.UpdateManeuversAsync([N.Maneuver("r", CarManeuverKind.Roundabout) with { RoundaboutExitNumber = 2 }]);
        Assert.Equal(CarNavigationState.Active, nav.State);
    }

    [Fact]
    public async Task Navigation_pause_resumes_with_maneuvers_or_reroute()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        await nav.StartTripAsync(trip, trip.Routes[0]);

        await nav.PauseAsync(CarNavigationPauseReason.Rerouting, "Calculating a new route");
        Assert.Equal(CarNavigationState.Paused, nav.State);
        Assert.Equal(CarNavigationPauseReason.Rerouting, f.Sink.States[^1].PauseReason);
        Assert.Equal("Calculating a new route", f.Sink.States[^1].PauseDescription?.Text);
        await nav.UpdateEstimatesAsync(N.Estimates(4, 8), null);
        Assert.Equal(CarNavigationState.Paused, nav.State);

        await nav.UpdateManeuversAsync([N.Maneuver("m3")]);
        Assert.Equal(CarNavigationState.Active, nav.State);
        Assert.Null(f.Sink.States[^1].PauseReason);
        Assert.True(Assert.IsType<CarNavigationChange.ManeuversUpdated>(Last(f)).Resumed);

        await nav.PauseAsync(CarNavigationPauseReason.Loading);
        await nav.UpdateManeuversAsync([]);
        Assert.Equal(CarNavigationState.Paused, nav.State);
        await nav.RerouteAsync(N.Route("detour", N.Maneuver("d1")));
        Assert.Equal(CarNavigationState.Active, nav.State);
    }

    [Fact]
    public async Task Navigation_reroute_replaces_the_route_and_maneuvers()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        await nav.StartTripAsync(trip, trip.Routes[0]);
        await nav.UpdateEstimatesAsync(N.Estimates(9, 20), N.Estimates(1, 2));

        var detour = N.Route("detour", N.Maneuver("d1"), N.Maneuver("d2"));
        await nav.RerouteAsync(detour);

        Assert.Same(detour, nav.Route);
        Assert.Same(trip, nav.Trip);
        Assert.Equal(["d1", "d2"], nav.UpcomingManeuvers.Select(m => m.Id));
        Assert.Equal(detour.Estimates, f.Sink.States[^1].TripEstimates);
        Assert.Null(f.Sink.States[^1].ManeuverEstimates);
        Assert.Same(detour, Assert.IsType<CarNavigationChange.Rerouted>(Last(f)).Route);
    }

    [Fact]
    public async Task Navigation_cancel_from_previewing_and_active()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();

        await nav.ShowTripPreviewsAsync([trip], null);
        await nav.CancelAsync();
        Assert.Equal(CarNavigationState.Idle, nav.State);
        Assert.Empty(nav.TripPreviews);

        await nav.ShowTripPreviewsAsync([trip], null);
        await nav.ShowTripPreviewsAsync([], null);
        Assert.Equal(CarNavigationState.Idle, nav.State);

        await nav.StartTripAsync(trip, trip.Routes[1]);
        await nav.PauseAsync(CarNavigationPauseReason.Arrived);
        await nav.CancelAsync();
        Assert.Equal(CarNavigationState.Idle, nav.State);
        Assert.Null(nav.Route);
        Assert.Equal(["Restore:Idle", "PreviewsShown", "Cancelled", "PreviewsShown", "PreviewsHidden", "TripStarted", "Paused", "Cancelled"], f.Sink.Calls);
    }

    [Fact]
    public async Task Navigation_state_is_kept_without_a_car_and_restored_on_attach()
    {
        using var f = new NavigationFixture();
        var nav = f.Controller;
        var trip = N.Trip();
        await nav.StartTripAsync(trip, trip.Routes[0]);
        await nav.UpdateEstimatesAsync(N.Estimates(3, 4), null);

        var attachment = await nav.AttachAsync(f.Sink);
        Assert.Equal(["Restore:Active"], f.Sink.Calls);
        Assert.Same(trip, f.Sink.States[0].Trip);
        Assert.Equal(N.Estimates(3, 4), f.Sink.States[0].TripEstimates);

        attachment.Dispose();
        await nav.FinishAsync();
        Assert.Single(f.Sink.Calls);
    }

    [Fact]
    public async Task Navigation_sink_failure_is_a_platform_exception_after_the_state_change()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var other = new RecordingSink(f.Dispatcher);
        await f.Controller.AttachAsync(other);
        f.Sink.Failure = new InvalidOperationException("native");
        var trip = N.Trip();

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => f.Controller.StartTripAsync(trip, trip.Routes[0]));

        Assert.Contains("native", ex.Message, StringComparison.Ordinal);
        Assert.Equal(CarNavigationState.Active, f.Controller.State);
        Assert.Equal("TripStarted", other.Calls[^1]);
    }

    [Fact]
    public async Task Navigation_car_end_goes_idle_and_calls_the_handler()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        var ended = 0;
        nav.EndedByCar += (_, _) => ended++;
        await nav.StartTripAsync(trip, trip.Routes[0]);

        await nav.OnEndedByCarAsync();

        Assert.Equal(CarNavigationState.Idle, nav.State);
        Assert.Equal("EndedByCar", f.Sink.Calls[^1]);
        Assert.Equal(["ended"], f.Handler.Calls);
        Assert.Equal(1, ended);

        // Already in Idle: a second report from the car changes nothing
        await nav.OnEndedByCarAsync();
        Assert.Single(f.Handler.Calls);
    }

    [Fact]
    public async Task Navigation_driver_actions_reach_the_handler()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;
        var trip = N.Trip();
        f.Handler.OnStart = () => nav.StartTripAsync(trip, trip.Routes[0]);
        await nav.ShowTripPreviewsAsync([trip], null);

        await nav.OnPreviewSelectedAsync(trip.Id, trip.Routes[1].Id);
        Assert.Same(trip, f.Sink.States[^1].SelectedPreview);
        await nav.OnStartRequestedAsync(trip.Id, trip.Routes[1].Id);
        Assert.Equal(CarNavigationState.Active, nav.State);
        await nav.OnManeuverSelectedAsync("m1");
        await nav.ShowAlertAsync(N.Alert());
        await nav.OnAlertSelectedAsync("alert");
        await nav.OnNavigationRequestedAsync(new CarNavigationRequest(new Uri("geo:0,0?q=Work")));
        await nav.OnAutoDriveRequestedAsync();
        // Unknown Id (e.g. a native object from before an update): without a handler
        await nav.OnManeuverSelectedAsync("gone");
        await nav.OnPreviewSelectedAsync("gone", "x");

        Assert.Equal(["preview:trip/eco", "start:trip/eco", "maneuver:m1", "alert:alert", "request:Work", "autodrive"], f.Handler.Calls);
        Assert.True(f.Handler.OnMainThread);
    }

    [Fact]
    public async Task Navigation_alert_is_kept_until_dismissed()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var nav = f.Controller;

        await nav.ShowAlertAsync(N.Alert());
        Assert.Equal("alert", f.Sink.States[^1].Alert?.Id);
        nav.OnAlertDismissed("other");
        Assert.Equal("alert", nav.Snapshot.Alert?.Id);
        nav.OnAlertDismissed("alert");
        Assert.Null(nav.Snapshot.Alert);
    }

    [Fact]
    public async Task Navigation_handler_errors_go_to_the_error_handler()
    {
        var handler = new ThrowingHandler();
        using var f = await new NavigationFixture(handler).AttachedAsync();

        await f.Controller.OnAutoDriveRequestedAsync();
        await f.Controller.OnNavigationRequestedAsync(new CarNavigationRequest(new Uri("geo:1,2")));

        Assert.Equal(["Navigation.AutoDriveRequested", "Navigation.NavigationRequested"], f.Errors.Errors.Select(e => e.Operation));
    }

    [Fact]
    public async Task Navigation_without_handler_ignores_driver_actions()
    {
        using var dispatcher = new FakeCarDispatcher();
        var controller = new CarNavigationController(dispatcher, new TestErrors(), () => null, new TestLogger());

        await controller.OnAutoDriveRequestedAsync();
        await controller.OnEndedByCarAsync();

        Assert.Equal(CarNavigationState.Idle, controller.State);
    }

    [Fact]
    public async Task Navigation_events_run_on_the_main_thread()
    {
        using var f = await new NavigationFixture().AttachedAsync();
        var onMain = true;
        var events = 0;
        f.Controller.StateChanged += (_, _) =>
        {
            events++;
            onMain &= !f.Dispatcher.IsDispatchRequired;
        };
        var trip = N.Trip();

        // Calls from the thread pool: the event is still raised on the main thread (review M14: the test passed without events)
        await Task.Run(() => f.Controller.StartTripAsync(trip, trip.Routes[0]), TestContext.Current.CancellationToken);
        await Task.Run(() => f.Controller.CancelAsync(), TestContext.Current.CancellationToken);

        Assert.Equal(2, events);
        Assert.True(onMain);
    }

    private static CarNavigationChange Last(NavigationFixture f) => f.Sink.Changes[^1];

    private sealed class ThrowingHandler : ICarNavigationHandler
    {
        public Task OnAutoDriveRequestedAsync() => throw new InvalidOperationException("app");

        public Task OnNavigationRequestedAsync(CarNavigationRequest request) => Task.FromException(new InvalidOperationException("app"));
    }
}
