using CarKit.Maui.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public sealed class RuntimeTests : IDisposable
{
    private readonly FakeCarDispatcher _dispatcher = new();
    private readonly RecordingErrorHandler _errors = new();

    public void Dispose() => _dispatcher.Dispose();

    [Fact]
    public void Resolve_uses_platform_application_services()
    {
        using var provider = Build(car => car.UseHandler<RecordingHandler>());

        Assert.Same(provider.GetRequiredService<CarKitRuntime>(), CarKitServices.ResolveRuntime(new FakePlatformApplication(provider)));
    }

    [Fact]
    public void Resolve_without_platform_application_explains_maui_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitServices.ResolveRuntime(null));
        Assert.Contains("IPlatformApplication.Current", error.Message, StringComparison.Ordinal);

        // MauiUIApplicationDelegate.Services is null until WillFinishLaunching
        error = Assert.Throws<InvalidOperationException>(() => CarKitServices.ResolveRuntime(new FakePlatformApplication(null)));
        Assert.Contains("MauiApp", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_without_UseCarKit_explains_registration()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => CarKitServices.ResolveRuntime(new FakePlatformApplication(provider)));
        Assert.Contains("UseCarKit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connect_starts_handler_and_disconnect_stops_it()
    {
        using var provider = Build(car => car.UseHandler<RecordingHandler>());
        var runtime = provider.GetRequiredService<CarKitRuntime>();
        var handler = (RecordingHandler)provider.GetRequiredService<ICarAppHandler>();
        var host = new TestCarHost();

        var connection = runtime.Connect(host);
        Assert.True(connection.Session.IsConnected);
        await connection.StartAsync();

        Assert.Equal(["Connected"], handler.Calls);
        Assert.Same(connection.Session, handler.Session);
        Assert.Equal(["SetRoot"], host.Calls);

        await connection.DisconnectAsync();
        Assert.Equal(["Connected", "Disconnected"], handler.Calls);
        Assert.False(connection.Session.IsConnected);
        Assert.Single(connection.Session.Stack);

        var second = runtime.Connect(new TestCarHost());
        Assert.NotSame(connection.Session, second.Session);
        await second.StartAsync();
        await second.DisconnectAsync();
        Assert.Equal(["Connected", "Disconnected", "Connected", "Disconnected"], handler.Calls);
        Assert.Empty(_errors.Errors);
    }

    [Fact]
    public async Task Disconnect_twice_is_safe()
    {
        using var provider = Build(car => car.UseHandler<RecordingHandler>());
        var connection = provider.GetRequiredService<CarKitRuntime>().Connect(new TestCarHost());
        await connection.StartAsync();

        await connection.DisconnectAsync();
        await connection.DisconnectAsync();

        Assert.Equal(["Connected", "Disconnected"], ((RecordingHandler)provider.GetRequiredService<ICarAppHandler>()).Calls);
    }

    [Fact]
    public async Task Handler_resolution_failure_is_reported()
    {
        using var provider = Build(car => car.UseHandler<RecordingHandler>(), services =>
            services.AddSingleton<ICarAppHandler>(_ => throw new InvalidOperationException("broken handler")));
        var connection = provider.GetRequiredService<CarKitRuntime>().Connect(new TestCarHost());

        await connection.StartAsync();
        await connection.DisconnectAsync();

        var (exception, context) = Assert.Single(_errors.Errors);
        Assert.Equal("broken handler", exception.Message);
        Assert.Equal("OnConnected", context.Operation);
    }

    [Fact]
    public async Task Handler_exception_is_reported_not_thrown()
    {
        using var provider = Build(car => car.UseHandler<RecordingHandler>(), services =>
            services.AddSingleton<ICarAppHandler>(new ThrowingHandler()));
        var connection = provider.GetRequiredService<CarKitRuntime>().Connect(new TestCarHost());

        await connection.StartAsync();

        Assert.Equal("OnConnected", Assert.Single(_errors.Errors).Context.Operation);
        await connection.DisconnectAsync();
    }

    // Review Focus 4: the completion handler never arrives, a disconnect ends the operation with CarNotConnectedException
    // (not ObjectDisposedException)
    [Fact]
    public async Task Disconnect_during_a_pending_native_call_ends_it_with_not_connected()
    {
        var handler = new PendingRootHandler();
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(new InlineCarDispatcher());
        services.AddSingleton<ICarErrorHandler>(_errors);
        services.AddSingleton<ICarAppHandler>(handler);
        CarKitAppBuilderExtensions.Register(services, _ => { }, CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();
        var host = new PendingCarHost();
        var connection = provider.GetRequiredService<CarKitRuntime>().Connect(host);

        var start = connection.StartAsync();
        Assert.False(start.IsCompleted);
        host.Calls.Close();
        await connection.DisconnectAsync();
        await start;

        Assert.IsType<CarNotConnectedException>(await handler.Failure.Task);
        Assert.Empty(_errors.Errors);
    }

    private ServiceProvider Build(Action<CarKitBuilder> configure, Action<IServiceCollection>? after = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(_dispatcher);
        services.AddSingleton<ICarErrorHandler>(_errors);
        CarKitAppBuilderExtensions.Register(services, configure, CarAppCategory.Parking);
        after?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class PendingRootHandler : ICarAppHandler
    {
        public TaskCompletionSource<Exception> Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
        {
            try
            {
                await session.SetRootAsync(new CarListTemplate("Root"));
            }
            catch (Exception ex)
            {
                Failure.TrySetResult(ex);
                throw;
            }
        }
    }

    private sealed class ThrowingHandler : ICarAppHandler
    {
        public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => throw new InvalidOperationException("handler failed");
    }
}
