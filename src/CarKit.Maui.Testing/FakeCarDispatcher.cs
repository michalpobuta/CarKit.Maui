using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace CarKit.Maui.Testing;

/// <summary>
/// A dispatcher with its own main thread for tests. Work runs on that thread one item at a time, awaits inside
/// it resume there, and code that hops off it (for example after <c>ConfigureAwait(false)</c>) sees
/// <see cref="IsDispatchRequired"/> as <see langword="true"/>, like on a device.
/// </summary>
public sealed class FakeCarDispatcher : ICarDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private int _dispatchCount;

    /// <summary>Creates the dispatcher and starts its main thread.</summary>
    public FakeCarDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "FakeCarDispatcher main thread" };
        _thread.Start();
    }

    /// <inheritdoc />
    public bool IsDispatchRequired => Thread.CurrentThread != _thread;

    /// <summary>Gets how many times work was dispatched.</summary>
    public int DispatchCount => Volatile.Read(ref _dispatchCount);

    /// <inheritdoc />
    public Task DispatchAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return DispatchAsync(async () =>
        {
            await work();
            return true;
        });
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031", Justification = "A work exception goes to the returned task.")]
    public Task<T> DispatchAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Interlocked.Increment(ref _dispatchCount);
        if (!IsDispatchRequired)
        {
            // On the main thread the work starts immediately, like MainThread.InvokeOnMainThreadAsync in MAUI
            return work();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(async () =>
        {
            try
            {
                completion.TrySetResult(await work());
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    /// <summary>Stops the main thread after the queued work.</summary>
    public void Dispose() => _queue.CompleteAdding();

    [SuppressMessage("Design", "CA1031", Justification = "A continuation error must not kill the tests' main thread.")]
    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(new MainThreadContext(this));
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Continuations store exceptions in their own tasks; only an infrastructure error gets here
            }
        }

        _queue.Dispose();
    }

    // A continuation after Dispose (background app work that returns to the main thread after the test ends) is
    // dropped: the queue is already closed or released by the main thread (Stage 7: previously ObjectDisposedException
    // crashed the test process)
    private void Post(Action action)
    {
        try
        {
            _queue.Add(action);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private sealed class MainThreadContext(FakeCarDispatcher owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => owner.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.IsDispatchRequired)
            {
                throw new NotSupportedException("Synchronous dispatch to the fake main thread is not supported.");
            }

            d(state);
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
