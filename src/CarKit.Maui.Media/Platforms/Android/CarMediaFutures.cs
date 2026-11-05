using AndroidX.Concurrent.Futures;
using Google.Common.Util.Concurrent;

namespace CarKit.Maui.Media;

// ListenableFuture for Media3 through CallbackToFutureAdapter (spec: ResolvableFuture is XAOBS001, the Guava binding
// has no Futures/SettableFuture). The completer is thread-safe; Media3 receives the result on its own loop.
internal static class CarMediaFutures
{
    public static IListenableFuture Done(Java.Lang.Object? value) => CallbackToFutureAdapter.GetFuture(new Resolver(completer => completer.Set(value)))!;

    // A future completed with a Java exception (e.g. UnsupportedOperationException where Media3 expects "not
    // supported")
    public static IListenableFuture Failed(Java.Lang.Throwable error) => CallbackToFutureAdapter.GetFuture(new Resolver(completer => completer.SetException(error)))!;

    // The result of a .NET task; an exception is turned by onError into a value (LibraryResult/SessionResult with an
    // error code), because Media3 expects a result, not an exception from the future
    public static IListenableFuture From<T>(Task<T> task, Func<T, Java.Lang.Object?> map, Func<Exception, Java.Lang.Object?> onError) =>
        CallbackToFutureAdapter.GetFuture(new Resolver(completer => task.ContinueWith(
            t =>
            {
                Java.Lang.Object? value;
                try
                {
                    value = t.IsCompletedSuccessfully ? map(t.Result) : onError(t.Exception?.GetBaseException() ?? new OperationCanceledException());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    value = onError(ex);
                }

                completer.Set(value);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default)))!;

    // Player command: the future completes once the command is handled, so that SimpleBasePlayer shows a placeholder
    // state until then
    public static IListenableFuture From(Task task) => From(task.ContinueWith(_ => 0, TaskScheduler.Default), _ => null, _ => null);

    private sealed class Resolver(Action<CallbackToFutureAdapter.Completer> attach) : Java.Lang.Object, CallbackToFutureAdapter.IResolver
    {
        public Java.Lang.Object? AttachCompleter(CallbackToFutureAdapter.Completer? p0)
        {
            attach(p0!);
            return new Java.Lang.String("CarKit.Media");
        }
    }
}
