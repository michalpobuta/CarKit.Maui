using Microsoft.Maui.ApplicationModel;

namespace CarKit.Maui;

// The MAUI main thread; on the main thread the work starts immediately (MainThread.InvokeOnMainThreadAsync)
internal sealed class MauiCarDispatcher : ICarDispatcher
{
    public bool IsDispatchRequired => !MainThread.IsMainThread;

    public Task DispatchAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return MainThread.InvokeOnMainThreadAsync(work);
    }

    public Task<T> DispatchAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return MainThread.InvokeOnMainThreadAsync(work);
    }
}
