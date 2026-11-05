using CarKit.Maui.Communication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Communication;

// Simulated VoIP engine: an outgoing call connects after 2 s, an incoming one right after it is answered (CarPlay, lock
// screen). The call manager comes from DI only when used (CallKit depends on this handler)
public sealed class SampleCallHandler(IServiceProvider services, ILogger<SampleCallHandler> logger) : ICarCallHandler
{
    public static readonly TimeSpan ConnectDelay = TimeSpan.FromSeconds(2);

    public Task<bool> OnAnswerAsync(CarCall call)
    {
        SampleLog.Info(logger, "answered " + (call.DisplayName ?? call.Handle.Value));
        return Task.FromResult(true);
    }

    public Task<bool> OnStartAsync(CarCall call)
    {
        SampleLog.Info(logger, "calling " + (call.DisplayName ?? call.Handle.Value));
        _ = ConnectLaterAsync(call.Id);
        return Task.FromResult(true);
    }

    public Task OnEndAsync(CarCall call)
    {
        SampleLog.Info(logger, "call with " + (call.DisplayName ?? call.Handle.Value) + " ended");
        return Task.CompletedTask;
    }

    private async Task ConnectLaterAsync(Guid callId)
    {
        await Task.Delay(ConnectDelay);
        var calls = services.GetRequiredService<ICarCallManager>();
        if (calls.Calls.Any(c => c.Id == callId && c.State is CarCallState.Dialing or CarCallState.Connecting))
        {
            MainThread.BeginInvokeOnMainThread(() => calls.ReportOutgoingCallConnected(callId));
        }
    }
}
