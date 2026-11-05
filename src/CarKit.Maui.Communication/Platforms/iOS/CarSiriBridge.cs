using CoreFoundation;
using Foundation;
using UIKit;

namespace CarKit.Maui.Communication;

// Bridge to the Intents Extension (spec: "SiriKit"): a CarConversationStore in the App Group container from
// <CarKitIosAppGroup>, receiving requests at startup, on return to the foreground and after the extension's Darwin
// notification → ICarMessageHandler. Without an App Group or without a container (simulator without the entitlement)
// the store is disabled. Conversation writes are serialized: read-merge-write of two parallel NotifyAsync calls lost
// conversations (review I3: 20 parallel → 2 in the file).
internal sealed class CarSiriBridge(CarCommunicationRuntime runtime) : IDisposable
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private CFNotificationObserverToken? _observer;
    private NSObject? _foreground;
    private bool _started;

    public CarConversationStore? Store { get; private set; }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        if (runtime.Options.AppGroup is not { } appGroup)
        {
            return;
        }

        if (NSFileManager.DefaultManager.GetContainerUrl(appGroup)?.Path is not { Length: > 0 } container)
        {
            CarCommunicationLog.Info(runtime.Logger,
                $"App Group '{appGroup}' has no container (missing com.apple.security.application-groups entitlement, e.g. a simulator build), so Siri requests are off");
            return;
        }

        UseStore(CarConversationStore.ForAppGroupContainer(container), appGroup);
    }

    // Device tests: the store in a temporary directory instead of the App Group container
    internal void UseStore(CarConversationStore store, string? appGroup)
    {
        Store = store;
        if (appGroup is not null)
        {
            _observer = CFNotificationCenter.Darwin.AddObserver(
                CarConversationStore.GetRequestsNotificationName(appGroup), null!, (_, _) => Drain(), CFNotificationSuspensionBehavior.DeliverImmediately);
        }

        _foreground ??= UIApplication.Notifications.ObserveWillEnterForeground((_, _) => Drain());
        Drain();
    }

    // The Darwin notification arrives on any thread; the router moves the handler to the main thread
    public void Drain()
    {
        if (Store is { } store)
        {
            _ = runtime.Messages.DrainAsync(store);
        }
    }

    public Task SaveConversationAsync(CarConversationItem conversation) => WriteAsync(async store =>
    {
        var saved = await store.LoadConversationsAsync().ConfigureAwait(false);
        await store.SaveConversationsAsync(saved.Where(c => c.Id != conversation.Id).Append(conversation).ToList()).ConfigureAwait(false);
    });

    public Task SaveConversationsAsync(IEnumerable<CarConversationItem> conversations) =>
        WriteAsync(store => store.SaveConversationsAsync(conversations));

    public void Dispose()
    {
        Stop();
        _writes.Dispose();
    }

    internal void Stop()
    {
        if (_observer is { } observer)
        {
            CFNotificationCenter.Darwin.RemoveObserver(observer);
            _observer = null;
        }

        _foreground?.Dispose();
        _foreground = null;
    }

    private async Task WriteAsync(Func<CarConversationStore, Task> write)
    {
        if (Store is not { } store)
        {
            return;
        }

        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await write(store).ConfigureAwait(false);
        }
        finally
        {
            _writes.Release();
        }
    }
}
