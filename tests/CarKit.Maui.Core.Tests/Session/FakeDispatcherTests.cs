using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Session;

// Stage 7 (found in the sample tests): app work in the background (e.g. a voice conversation simulation) returns to the
// main thread. FakeCarDispatcher after the host is disposed: the continuation must not crash the test process
// (ObjectDisposedException in Post)
public class FakeDispatcherTests
{
    [Fact]
    public async Task Late_continuation_after_dispose_is_dropped()
    {
        var dispatcher = new FakeCarDispatcher();
        var started = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        var resumed = false;
        _ = dispatcher.DispatchAsync(async () =>
        {
            started.SetResult();
            await gate.Task;
            resumed = true;
        });
        await started.Task;

        dispatcher.Dispose();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var exception = Record.Exception(gate.SetResult);

        Assert.Null(exception);
        Assert.False(resumed);
    }
}
