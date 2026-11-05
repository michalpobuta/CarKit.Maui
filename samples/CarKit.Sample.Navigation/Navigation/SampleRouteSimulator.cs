using CarKit.Maui;
using CarKit.Maui.Navigation;

namespace CarKit.Sample.Navigation;

// Simulated driving (spec: "Navigation module decisions (Stage 8)", Sample): a preview of two routes → start →
// maneuvers with estimates and voice prompts → a traffic jam alert → pause (rerouting) → a detour (with a reason,
// Stage 9) → a stop on a route with two segments (Stage 9) → arrival → finish. The same navigation session drives the
// car (CarPlay, Android Auto, Dashboard, cluster) and the phone screen. The step delay is injected (tests: zero).
public sealed class SampleRouteSimulator
{
    private readonly ICarNavigationSession _navigation;
    private readonly ICarVoicePrompts? _voice;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _run;

    public SampleRouteSimulator(ICarNavigationSession navigation, ICarVoicePrompts? voice = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _navigation = navigation;
        _voice = voice;
        _delay = delay ?? Task.Delay;
    }

    // Simulation steps for the log and the phone screen
    public event EventHandler<string>? Progress;

    public SampleDestination? Destination { get; private set; }

    // Driving progress 0–1 (the map renderer draws the car on the route)
    public double RouteProgress { get; private set; }

    // Android Auto onAutoDriveEnabled: start the first route as soon as a preview appears
    public bool AutoDrive { get; set; }

    public Task? Running { get; private set; }

    public TimeSpan Step { get; set; } = TimeSpan.FromSeconds(2);

    public async Task PreviewAsync(SampleDestination destination)
    {
        Stop();
        if (_navigation.State is CarNavigationState.Active or CarNavigationState.Paused)
        {
            await _navigation.CancelAsync();
        }

        Destination = destination;
        RouteProgress = 0;
        var trip = SampleRoutes.Trip(destination);
        await _navigation.ShowTripPreviewsAsync([trip], trip);
        Report($"preview: {destination.Name} ({trip.Routes.Count} routes)");
        if (AutoDrive)
        {
            Start(trip, trip.Routes[0]);
        }
    }

    // The driver pressed Go/Start (ICarNavigationHandler) or a phone button
    public void Start(CarTrip trip, CarRouteChoice route)
    {
        Stop();
        var run = new CancellationTokenSource();
        lock (_lock)
        {
            _run = run;
        }

        Running = DriveAsync(trip, route, run.Token);
    }

    public void Stop()
    {
        CancellationTokenSource? run;
        lock (_lock)
        {
            run = _run;
            _run = null;
        }

        run?.Cancel();
        run?.Dispose();
        _voice?.CancelAll();
    }

    private async Task DriveAsync(CarTrip trip, CarRouteChoice route, CancellationToken ct)
    {
        try
        {
            await _navigation.StartTripAsync(trip, route);
            Report("start: " + route.Summary.Text);
            var maneuvers = route.Maneuvers.ToList();
            var rerouted = false;
            var total = maneuvers.Count;
            var done = 0;
            while (maneuvers.Count > 0)
            {
                var next = maneuvers[0];
                Speak(next.Instruction.Text, ct);
                // Three legs to the maneuver: the maneuver and destination estimates decrease
                for (var leg = 3; leg >= 1; leg--)
                {
                    await _delay(Step, ct);
                    var remaining = maneuvers.Sum(m => Kilometers(m)) - (Kilometers(next) * (3 - leg) / 3);
                    await _navigation.UpdateEstimatesAsync(
                        SampleRoutes.Estimates(remaining, remaining * 2),
                        SampleRoutes.Estimates(Kilometers(next) * leg / 3, Kilometers(next) * leg / 1.5));
                    RouteProgress = Math.Min(1, (done + ((3 - leg + 1) / 3.0)) / total);
                }

                // After the first maneuver of the fast route: a traffic jam → an alert, rerouting and a detour
                if (!rerouted && route.Id.StartsWith("fast", StringComparison.Ordinal) && done == 1 && Destination is { } destination)
                {
                    rerouted = true;
                    await _navigation.ShowAlertAsync(new CarNavigationAlert("Traffic jam on the route", new CarAction("Detour") { Id = "alert-detour" })
                    {
                        Id = "alert-traffic",
                        Subtitle = "Calculating a detour",
                        Image = CarImage.FromSystemSymbol("exclamationmark.triangle.fill", "ic_nav_flag"),
                        SecondaryAction = new CarAction("Stay") { Id = "alert-stay" },
                    });
                    Report("alert: traffic jam");
                    await _navigation.PauseAsync(CarNavigationPauseReason.Rerouting, "Calculating a detour");
                    await _delay(Step, ct);
                    var detour = SampleRoutes.Detour(destination);
                    // Stage 9: the reason for the detour for the car (CarPlay iOS 26.4)
                    await _navigation.RerouteAsync(detour, CarRerouteReason.AlternateRoute);
                    Report("detour: " + detour.Summary.Text);
                    maneuvers = [.. detour.Maneuvers];
                    total = done + maneuvers.Count;
                    continue;
                }

                maneuvers.RemoveAt(0);
                done++;
                await NextSegmentAsync(next);
                if (maneuvers.Count > 0)
                {
                    await _navigation.UpdateManeuversAsync(maneuvers);
                }
            }

            RouteProgress = 1;
            Speak("You have arrived", ct);
            await _navigation.PauseAsync(CarNavigationPauseReason.Arrived, "You have arrived");
            Report("arrived");
            await _delay(Step, ct);
            await _navigation.FinishAsync();
            Report("finished");
        }
        catch (OperationCanceledException)
        {
            Report("simulation stopped");
        }
        catch (InvalidOperationException ex)
        {
            // The car or the phone ended navigation during a step
            Report("simulation interrupted: " + ex.Message);
        }
    }

    private static double Kilometers(CarManeuver maneuver) =>
        maneuver.InitialEstimates?.DistanceRemaining is { } distance
            ? distance.Unit == CarDistanceUnit.Meters ? distance.Value / 1000 : distance.Value
            : 0.5;

    private void Speak(string text, CancellationToken ct)
    {
        if (_voice is null)
        {
            return;
        }

        // A prompt does not hold up driving; a speech engine error goes to the log only
        _ = _voice.SpeakAsync(text, ct).ContinueWith(
            task => Report("voice: " + (task.IsCompletedSuccessfully ? text : task.Exception?.InnerException?.Message ?? "canceled")),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void Report(string message) => Progress?.Invoke(this, message);

    // Stage 9: car requests during route sharing (the sample ICarNavigationHandler) go to the log and the phone screen
    public void ReportCarRequest(string message) => Report("car: " + message);

    // Stage 9: after the last maneuver of a segment (a stop), guidance moves on to the next route segment
    private async Task NextSegmentAsync(CarManeuver passed)
    {
        if (_navigation.Route is not { Segments.IsEmpty: false } route)
        {
            return;
        }

        var index = _navigation.CurrentSegmentIndex;
        if (index + 1 < route.Segments.Count && route.Segments[index].Maneuvers is { IsEmpty: false } legManeuvers && legManeuvers[^1].Id == passed.Id)
        {
            await _navigation.UpdateCurrentSegmentAsync(index + 1);
            Report("stop: " + route.Segments[index].Destination.Name?.Text);
        }
    }
}
