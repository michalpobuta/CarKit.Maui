using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Shared by the hosts: creates a session on ICarHost and drives the connection lifecycle with the app's handler.
// Registered by UseCarKit; the CarPlay scene and CarAppService get it from IPlatformApplication.Current.Services.
internal sealed class CarKitRuntime
{
    private readonly ILogger _sessionLogger;

    public CarKitRuntime(IServiceProvider services)
    {
        Services = services;
        Options = services.GetRequiredService<CarKitOptions>();
        Android = services.GetRequiredService<CarKitAndroidOptions>();
        Dispatcher = services.GetRequiredService<ICarDispatcher>();
        ErrorHandler = services.GetRequiredService<ICarErrorHandler>();
        var loggers = CarKitAppBuilderExtensions.LoggerFactory(services);
        Logger = loggers.CreateLogger("CarKit.Hosting");
        _sessionLogger = loggers.CreateLogger("CarKit.Session");
    }

    // App services: modules (Stage 5) register template renderers here, which the hosts pass to the render contexts
    public IServiceProvider Services { get; }

    public CarKitOptions Options { get; }

    public CarKitAndroidOptions Android { get; }

    public ICarDispatcher Dispatcher { get; }

    public ICarErrorHandler ErrorHandler { get; }

    public ILogger Logger { get; }

    // The host gets the session (Connection.Session) before StartAsync so the renderers can call the handlers
    public CarKitConnection Connect(ICarHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new CarKitConnection(this, new CarSession(host, Options, Dispatcher, ErrorHandler, _sessionLogger));
    }

    internal ICarAppHandler ResolveHandler() => Services.GetRequiredService<ICarAppHandler>();
}

// One car connection. StartAsync and DisconnectAsync do not throw: errors go to ICarErrorHandler, because native
// callbacks (the CarPlay scene, the Session lifecycle) call them and an exception would crash the process.
internal sealed class CarKitConnection : IDisposable
{
    private readonly CarKitRuntime _runtime;
    private ICarAppHandler? _handler;
    private int _disconnected;

    internal CarKitConnection(CarKitRuntime runtime, CarSession session)
    {
        _runtime = runtime;
        Session = session;
    }

    public CarSession Session { get; }

    public async Task StartAsync()
    {
        try
        {
            _handler = _runtime.ResolveHandler();
            await Session.StartAsync(_handler).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("OnConnected", null));
        }
    }

    public async Task DisconnectAsync()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 1)
        {
            return;
        }

        try
        {
            await Session.StopAsync(_handler ?? NoHandler.Instance).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _runtime.ErrorHandler.HandleError(ex, new CarErrorContext("OnDisconnected", null));
        }

        // No Dispose of the session: an operation waiting for a native call completed by the disconnect releases the gate
        // only after DisconnectAsync (ObjectDisposedException instead of CarNotConnectedException). The gate and the token
        // without registrations hold no unmanaged resources.
    }

    // Only when no session operation is in progress anymore (e.g. tests)
    public void Dispose() => Session.Dispose();

    // The handler was not created (DI error); the disconnect must go through anyway
    private sealed class NoHandler : ICarAppHandler
    {
        public static readonly NoHandler Instance = new();

        public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => Task.CompletedTask;
    }
}
