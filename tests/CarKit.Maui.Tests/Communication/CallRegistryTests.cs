using CarKit.Maui.Communication;
using CarKit.Maui.Tests.Media;
using Xunit;

namespace CarKit.Maui.Tests.Communication;

// CallKit call state machine (Review Focus 5)
public class CallRegistryTests
{
    private static readonly CarCallHandle _anna = new(CarCallHandleType.PhoneNumber, "+15550100200");

    private static CarCall Incoming() => new(Guid.NewGuid(), _anna, CarCallDirection.Incoming) { State = CarCallState.Ringing, DisplayName = "Anna" };

    private static CarCall Outgoing() => new(Guid.NewGuid(), _anna, CarCallDirection.Outgoing) { State = CarCallState.Dialing };

    [Fact]
    public void Incoming_call_rings_then_answers()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var changes = new List<CarCallState>();
        registry.Changed += (_, call) => changes.Add(call.State);
        var call = registry.Add(Incoming());

        registry.Transition(call.Id, CarCallState.Active);

        Assert.Equal([CarCallState.Ringing, CarCallState.Active], changes);
        Assert.Equal(CarCallState.Active, Assert.Single(registry.Calls).State);
    }

    [Fact]
    public void Outgoing_call_dials_connects_and_ends()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var call = registry.Add(Outgoing());

        registry.Transition(call.Id, CarCallState.Connecting);
        registry.Transition(call.Id, CarCallState.Active);
        var ended = registry.Transition(call.Id, CarCallState.Ended, CarCallEndReason.RemoteEnded);

        Assert.Equal(CarCallState.Ended, ended!.State);
        Assert.Equal(CarCallEndReason.RemoteEnded, ended.EndReason);
        Assert.Empty(registry.Calls);
        Assert.Null(registry.Find(call.Id));
    }

    [Fact]
    public void Hold_and_resume()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var call = registry.Add(Incoming());
        registry.Transition(call.Id, CarCallState.Active);

        Assert.Equal(CarCallState.Held, registry.Transition(call.Id, CarCallState.Held)!.State);
        Assert.Equal(CarCallState.Active, registry.Transition(call.Id, CarCallState.Active)!.State);
        Assert.True(registry.SetMuted(call.Id, true)!.IsMuted);
        Assert.True(registry.SetMuted(call.Id, true)!.IsMuted);
    }

    [Theory]
    [InlineData(CarCallState.Ringing, CarCallState.Held)]
    [InlineData(CarCallState.Ringing, CarCallState.Connecting)]
    [InlineData(CarCallState.Dialing, CarCallState.Held)]
    [InlineData(CarCallState.Active, CarCallState.Connecting)]
    [InlineData(CarCallState.Active, CarCallState.Ringing)]
    public void Invalid_transitions_throw(CarCallState from, CarCallState to)
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var call = registry.Add(from is CarCallState.Ringing or CarCallState.Active ? Incoming() : Outgoing());
        if (from == CarCallState.Active)
        {
            registry.Transition(call.Id, CarCallState.Active);
        }

        Assert.Throws<InvalidOperationException>(() => registry.Transition(call.Id, to));
    }

    [Fact]
    public void Ending_twice_is_ignored()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var changes = 0;
        registry.Changed += (_, _) => changes++;
        var call = registry.Add(Incoming());

        registry.Transition(call.Id, CarCallState.Ended);
        Assert.Null(registry.Transition(call.Id, CarCallState.Ended, CarCallEndReason.Failed));
        Assert.Null(registry.SetMuted(call.Id, true));

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Unknown_calls_and_bad_starts_throw()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var call = registry.Add(Incoming());

        Assert.Throws<InvalidOperationException>(() => registry.Transition(Guid.NewGuid(), CarCallState.Ended));
        Assert.Throws<InvalidOperationException>(() => registry.SetMuted(Guid.NewGuid(), true));
        Assert.Throws<InvalidOperationException>(() => registry.Add(call));
        Assert.Throws<ArgumentException>(() => registry.Add(Incoming() with { State = CarCallState.Active }));
        Assert.Throws<ArgumentException>(() => new CarCallHandle(CarCallHandleType.Generic, " "));
    }

    [Fact]
    public void Ended_call_defaults_to_a_local_end()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var call = registry.Add(Outgoing());

        Assert.Equal(CarCallEndReason.Local, registry.Transition(call.Id, CarCallState.Ended)!.EndReason);
    }

    [Fact]
    public void Changes_arrive_on_the_main_thread()
    {
        var dispatcher = new ManualDispatcher { OnMainThread = false };
        var registry = new CarCallRegistry(dispatcher);
        var changes = new List<CarCallState>();
        registry.Changed += (_, call) => changes.Add(call.State);

        var call = registry.Add(Incoming());
        registry.Transition(call.Id, CarCallState.Active);
        Assert.Empty(changes);
        dispatcher.Pump();

        Assert.Equal([CarCallState.Ringing, CarCallState.Active], changes);
    }

    [Fact]
    public async Task Unsupported_manager_explains_the_gate()
    {
        var manager = CarUnsupportedCallManager.Instance;
        manager.CallChanged += (_, _) => { };

        Assert.False(manager.IsSupported);
        Assert.Empty(manager.Calls);
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.ReportIncomingCallAsync(_anna));
        Assert.Contains("Core-Telecom", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.StartOutgoingCallAsync(_anna));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.EndCallAsync(Guid.Empty));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.SetHeldAsync(Guid.Empty, true));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => manager.SetMutedAsync(Guid.Empty, true));
        Assert.Throws<PlatformNotSupportedException>(() => manager.ReportOutgoingCallConnected(Guid.Empty));
        Assert.Throws<PlatformNotSupportedException>(() => manager.ReportCallEnded(Guid.Empty, CarCallEndReason.Failed));
    }

    [Fact]
    public async Task Default_call_handler_accepts_every_action()
    {
        ICarCallHandler handler = CarDefaultCallHandler.Instance;
        var call = Incoming();

        Assert.True(await handler.OnAnswerAsync(call));
        Assert.True(await handler.OnStartAsync(call));
        Assert.True(await handler.OnSetHeldAsync(call, true));
        Assert.True(await handler.OnSetMutedAsync(call, true));
        await handler.OnEndAsync(call);
        handler.OnAudioSessionActivated();
        handler.OnAudioSessionDeactivated();
    }
}
