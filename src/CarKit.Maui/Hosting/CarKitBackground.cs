namespace CarKit.Maui;

// The host reports native events (back, dismiss, theme, disconnect) without waiting (ICarHost contract);
// a failure of such a task goes to ICarErrorHandler instead of being left unobserved.
internal static class CarKitBackground
{
    public static void Forget(this Task task, ICarErrorHandler errors, string operation)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(errors);
        task.ContinueWith(
            t => errors.HandleError(t.Exception!.GetBaseException(), new CarErrorContext(operation, null)),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
