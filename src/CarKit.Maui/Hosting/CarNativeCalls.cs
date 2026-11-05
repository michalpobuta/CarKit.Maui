namespace CarKit.Maui;

// Tracks the host's native calls (ICarHost contract): after a disconnect the platform's completion handler may
// never arrive, so Close() completes pending tasks with CarNotConnectedException and rejects new ones at once.
internal sealed class CarNativeCalls
{
    private readonly Lock _lock = new();
    private readonly HashSet<Action> _pending = [];
    private bool _closed;

    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _closed;
            }
        }
    }

    public Task Run(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return Run(async () =>
        {
            await call().ConfigureAwait(false);
            return true;
        });
    }

    public async Task<T> Run<T>(Func<Task<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var disconnected = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action fail = () => disconnected.TrySetException(new CarNotConnectedException("The car disconnected before the platform finished the call."));
        lock (_lock)
        {
            if (_closed)
            {
                throw new CarNotConnectedException("The car is not connected.");
            }

            _pending.Add(fail);
        }

        try
        {
            var native = call();
            if (await Task.WhenAny(native, disconnected.Task).ConfigureAwait(false) == native)
            {
                return await native.ConfigureAwait(false);
            }

            // A late native failure must not go unobserved
            _ = native.ContinueWith(static t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return await disconnected.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _pending.Remove(fail);
            }
        }
    }

    public void Close()
    {
        Action[] pending;
        lock (_lock)
        {
            _closed = true;
            pending = [.. _pending];
            _pending.Clear();
        }

        foreach (var fail in pending)
        {
            fail();
        }
    }
}
