using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public class BackgroundTests
{
    [Fact]
    public async Task Forget_reports_faults_to_error_handler()
    {
        var errors = new RecordingErrorHandler();
        var failing = new TaskCompletionSource();

        failing.Task.Forget(errors, "HostPopped");
        failing.SetException(new InvalidOperationException("late"));
        await Task.Yield();

        var (exception, context) = Assert.Single(errors.Errors);
        Assert.Equal("late", exception.Message);
        Assert.Equal("HostPopped", context.Operation);
    }

    [Fact]
    public void Forget_ignores_success_and_cancellation()
    {
        var errors = new RecordingErrorHandler();

        Task.CompletedTask.Forget(errors, "Ok");
        Task.FromCanceled(new CancellationToken(canceled: true)).Forget(errors, "Cancelled");

        Assert.Empty(errors.Errors);
    }
}
