using CarKit.Maui.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public sealed class HostEventsTests : IDisposable
{
    private readonly List<Action> _mainLoop = [];
    private readonly FakeCarDispatcher _dispatcher = new();
    private readonly RecordingErrorHandler _errors = new();

    public void Dispose() => _dispatcher.Dispose();

    [Fact]
    public void Posted_work_runs_on_the_next_main_loop_turn_not_inline()
    {
        var events = new CarHostEvents(_mainLoop.Add);
        var ran = false;

        events.Post(() => ran = true);
        Assert.False(ran);

        RunMainLoop();
        Assert.True(ran);
    }

    [Fact]
    public void Work_posted_before_close_is_dropped()
    {
        var events = new CarHostEvents(_mainLoop.Add);
        var ran = false;

        events.Post(() => ran = true);
        events.Close();
        events.Post(() => ran = true);
        RunMainLoop();

        Assert.False(ran);
        Assert.True(events.IsClosed);
    }

    // The Car App Library destroys screens before the session's ON_DESTROY: "back" reports from that sequence must not eat
    // the stack
    [Fact]
    public async Task Disconnect_keeps_the_stack_when_the_host_destroys_screens_first()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(_dispatcher);
        services.AddSingleton<ICarErrorHandler>(_errors);
        CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<StackHandler>(), CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();
        var events = new CarHostEvents(_mainLoop.Add);
        var connection = provider.GetRequiredService<CarKitRuntime>().Connect(new TestCarHost());
        await connection.StartAsync();
        Assert.Equal(2, connection.Session.Stack.Count);

        // Order from a disconnect: the screen is destroyed (report deferred), then the session's ON_DESTROY
        var details = connection.Session.Stack[1].Id;
        events.Post(() => connection.Session.OnHostPoppedAsync(details).Forget(_errors, "HostPopped"));
        events.Close();
        await connection.DisconnectAsync();
        RunMainLoop();

        Assert.Equal(2, connection.Session.Stack.Count);
        Assert.Empty(_errors.Errors);
    }

    private void RunMainLoop()
    {
        var work = _mainLoop.ToArray();
        _mainLoop.Clear();
        foreach (var action in work)
        {
            action();
        }
    }

    private sealed class StackHandler : ICarAppHandler
    {
        public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
        {
            await session.SetRootAsync(new CarListTemplate("Root"));
            await session.PushAsync(new CarListTemplate("Details"));
        }
    }
}
