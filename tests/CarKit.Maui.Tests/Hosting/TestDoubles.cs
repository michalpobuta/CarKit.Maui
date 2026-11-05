using Microsoft.Maui;

namespace CarKit.Maui.Tests.Hosting;

internal sealed class RecordingHandler : ICarAppHandler
{
    public List<string> Calls { get; } = [];

    public ICarSession? Session { get; private set; }

    public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        Session = session;
        Calls.Add("Connected");
        await session.SetRootAsync(new CarListTemplate("Root"));
    }

    public Task OnDisconnectedAsync(ICarSession session)
    {
        Calls.Add("Disconnected");
        return Task.CompletedTask;
    }
}

internal sealed class OtherHandler : ICarAppHandler
{
    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class RecordingErrorHandler : ICarErrorHandler
{
    public List<(Exception Exception, CarErrorContext Context)> Errors { get; } = [];

    public void HandleError(Exception exception, CarErrorContext context) => Errors.Add((exception, context));
}

// Minimal host: records the calls, no platform simulation
internal sealed class TestCarHost : ICarHost
{
    public List<string> Calls { get; } = [];

    public CarPlatform Platform => CarPlatform.CarPlay;

    public ICarCapabilities Capabilities { get; } = new CarCapabilities(CarPlatform.CarPlay, new Version(26, 4), null);

    public CarContentStyle ContentStyle => CarContentStyle.Light;

    public Task SetRootAsync(CarTemplate root, bool animated) => Record("SetRoot");

    public Task PushAsync(CarTemplate template, bool animated) => Record("Push");

    public Task PopAsync(bool animated) => Record("Pop");

    public Task PopToAsync(CarTemplate template, bool animated) => Record("PopTo");

    public Task PopToRootAsync(bool animated) => Record("PopToRoot");

    public Task PresentAsync(CarTemplate modal, bool animated) => Record("Present");

    public Task DismissAsync(bool animated) => Record("Dismiss");

    public Task UpdateAsync(CarTemplate previous, CarTemplate updated) => Record("Update");

    public TNative? GetNative<TNative>() where TNative : class => this as TNative;

    private Task Record(string call)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakePlatformApplication(IServiceProvider? services) : IPlatformApplication
{
    public IServiceProvider Services => services!;

    public IApplication Application => throw new NotSupportedException();
}

// MAUI main thread: work on the main thread starts immediately (MainThread.InvokeOnMainThreadAsync)
internal sealed class InlineCarDispatcher : ICarDispatcher
{
    public bool IsDispatchRequired => false;

    public Task DispatchAsync(Func<Task> work) => work();

    public Task<T> DispatchAsync<T>(Func<Task<T>> work) => work();
}

// A host whose SetRoot does not finish until a disconnect closes it (a CarPlay completion handler that never arrives)
internal sealed class PendingCarHost : ICarHost
{
    private readonly TaskCompletionSource<bool> _never = new();

    public CarNativeCalls Calls { get; } = new();

    public CarPlatform Platform => CarPlatform.CarPlay;

    public ICarCapabilities Capabilities { get; } = new CarCapabilities(CarPlatform.CarPlay, new Version(26, 4), null);

    public CarContentStyle ContentStyle => CarContentStyle.Light;

    public Task SetRootAsync(CarTemplate root, bool animated) => Calls.Run(() => _never.Task);

    public Task PushAsync(CarTemplate template, bool animated) => Task.CompletedTask;

    public Task PopAsync(bool animated) => Task.CompletedTask;

    public Task PopToAsync(CarTemplate template, bool animated) => Task.CompletedTask;

    public Task PopToRootAsync(bool animated) => Task.CompletedTask;

    public Task PresentAsync(CarTemplate modal, bool animated) => Task.CompletedTask;

    public Task DismissAsync(bool animated) => Task.CompletedTask;

    public Task UpdateAsync(CarTemplate previous, CarTemplate updated) => Task.CompletedTask;

    public TNative? GetNative<TNative>() where TNative : class => null;
}
