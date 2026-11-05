using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public class NativeCallsTests
{
    [Fact]
    public async Task Completed_call_passes_result_through()
    {
        var calls = new CarNativeCalls();

        Assert.Equal(42, await calls.Run(() => Task.FromResult(42)));
        await calls.Run(() => Task.CompletedTask);
    }

    [Fact]
    public async Task Native_failure_passes_through()
    {
        var calls = new CarNativeCalls();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => calls.Run(() => Task.FromException(new InvalidOperationException("native"))));
        Assert.Equal("native", error.Message);
    }

    [Fact]
    public async Task Close_faults_pending_calls_with_not_connected()
    {
        var calls = new CarNativeCalls();
        var never = new TaskCompletionSource<bool>();
        var pending = calls.Run(() => never.Task);
        var pendingVoid = calls.Run(() => (Task)new TaskCompletionSource().Task);
        Assert.False(pending.IsCompleted);

        calls.Close();

        Assert.True(calls.IsClosed);
        await Assert.ThrowsAsync<CarNotConnectedException>(() => pending);
        await Assert.ThrowsAsync<CarNotConnectedException>(() => pendingVoid);

        // A late native completion changes nothing anymore
        never.SetResult(true);
        await Assert.ThrowsAsync<CarNotConnectedException>(() => pending);
    }

    [Fact]
    public async Task Run_after_close_fails_fast()
    {
        var calls = new CarNativeCalls();
        calls.Close();
        var started = false;

        await Assert.ThrowsAsync<CarNotConnectedException>(() => calls.Run(() =>
        {
            started = true;
            return Task.CompletedTask;
        }));
        Assert.False(started);
    }
}
