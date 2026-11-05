using CarKit.Maui.Communication;
using CarKit.Maui.Tests.Media;
using Xunit;

namespace CarKit.Maui.Tests.Communication;

// CallKit system actions → ICarCallHandler → state (review I1, I2, I7; Review Focus 5)
public class CallActionsTests
{
    private static readonly CarCallHandle _anna = new(CarCallHandleType.PhoneNumber, "+15550100200");

    private sealed class Handler : ICarCallHandler
    {
        public Func<CarCall, Task<bool>>? Start { get; init; }

        public bool Accept { get; init; } = true;

        public Exception? Throw { get; init; }

        public List<string> Calls { get; } = [];

        public Task<bool> OnAnswerAsync(CarCall call) => Run("answer", call);

        public Task<bool> OnStartAsync(CarCall call) => Start?.Invoke(call) ?? Run("start", call);

        public Task OnEndAsync(CarCall call) => Run("end", call);

        public Task<bool> OnSetHeldAsync(CarCall call, bool onHold) => Run("hold:" + onHold, call);

        public Task<bool> OnSetMutedAsync(CarCall call, bool muted) => Run("mute:" + muted, call);

        private Task<bool> Run(string name, CarCall call)
        {
            Calls.Add(name);
            return Throw is { } ex ? Task.FromException<bool>(ex) : Task.FromResult(Accept);
        }
    }

    private static (CarCallRegistry Registry, CarCallActions Actions, RecordingErrors Errors) Create(Handler handler)
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var errors = new RecordingErrors();
        return (registry, new CarCallActions(registry, handler, errors), errors);
    }

    private static CarCall Incoming(CarCallRegistry registry) =>
        registry.Add(new CarCall(Guid.NewGuid(), _anna, CarCallDirection.Incoming) { State = CarCallState.Ringing });

    private static CarCall Outgoing(CarCallRegistry registry) =>
        registry.Add(new CarCall(Guid.NewGuid(), _anna, CarCallDirection.Outgoing) { State = CarCallState.Dialing });

    [Fact]
    public async Task Unknown_call_action_fails()
    {
        var handler = new Handler();
        var (_, actions, errors) = Create(handler);

        Assert.False(await actions.PerformAsync(Guid.NewGuid(), CarCallActionKind.Answer));
        Assert.False(await actions.PerformAsync(Guid.NewGuid(), CarCallActionKind.End));
        Assert.Empty(handler.Calls);
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public async Task Answer_connects_an_incoming_call()
    {
        var (registry, actions, _) = Create(new Handler());
        var call = Incoming(registry);

        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Answer));

        Assert.Equal(CarCallState.Active, registry.Find(call.Id)!.State);
    }

    [Fact]
    public async Task Refused_answer_fails_and_ends_the_call()
    {
        var (registry, actions, _) = Create(new Handler { Accept = false });
        var call = Incoming(registry);

        Assert.False(await actions.PerformAsync(call.Id, CarCallActionKind.Answer));

        Assert.Null(registry.Find(call.Id));
    }

    [Fact]
    public async Task Start_moves_the_call_to_connecting()
    {
        var (registry, actions, _) = Create(new Handler());
        var call = Outgoing(registry);

        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Start));

        Assert.Equal(CarCallState.Connecting, registry.Find(call.Id)!.State);
    }

    // I2: the app reports the call already in OnStartAsync: the call stays active and the start action succeeds
    [Fact]
    public async Task Start_connected_during_the_handler_keeps_the_call()
    {
        CarCallRegistry? registry = null;
        var handler = new Handler
        {
            Start = call =>
            {
                registry!.ConnectOutgoing(call.Id);
                return Task.FromResult(true);
            },
        };
        (registry, var actions, var errors) = Create(handler);
        var call = Outgoing(registry);

        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Start));

        Assert.Equal(CarCallState.Active, registry.Find(call.Id)!.State);
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public async Task Handler_exception_goes_to_the_error_handler_and_fails()
    {
        var (registry, actions, errors) = Create(new Handler { Throw = new TimeoutException("VoIP") });
        var call = Outgoing(registry);

        Assert.False(await actions.PerformAsync(call.Id, CarCallActionKind.Start));

        Assert.Null(registry.Find(call.Id));
        var (exception, context) = Assert.Single(errors.Errors);
        Assert.IsType<TimeoutException>(exception);
        Assert.Equal("Communication.Start", context.Operation);
    }

    [Fact]
    public async Task End_hold_and_mute_change_the_state()
    {
        var handler = new Handler();
        var (registry, actions, _) = Create(handler);
        var call = Incoming(registry);
        await actions.PerformAsync(call.Id, CarCallActionKind.Answer);

        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Hold, true));
        Assert.Equal(CarCallState.Held, registry.Find(call.Id)!.State);
        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Hold, false));
        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.Mute, true));
        Assert.True(registry.Find(call.Id)!.IsMuted);
        Assert.True(await actions.PerformAsync(call.Id, CarCallActionKind.End));

        Assert.Null(registry.Find(call.Id));
        Assert.Equal(["answer", "hold:True", "hold:False", "mute:True", "end"], handler.Calls);
    }

    [Fact]
    public async Task Refused_hold_keeps_the_call_active()
    {
        var (registry, actions, _) = Create(new Handler { Accept = false });
        var call = Incoming(registry);
        registry.Transition(call.Id, CarCallState.Active);

        Assert.False(await actions.PerformAsync(call.Id, CarCallActionKind.Hold, true));

        Assert.Equal(CarCallState.Active, registry.Find(call.Id)!.State);
    }

    // I1 (Review Focus 5): ReportOutgoingCallConnected only for an outgoing call that is connecting
    [Fact]
    public void Connect_outgoing_rejects_incoming_held_and_connected_calls()
    {
        var registry = new CarCallRegistry(new InlineDispatcher());
        var incoming = Incoming(registry);
        var held = Outgoing(registry);
        registry.ConnectOutgoing(held.Id);
        registry.Transition(held.Id, CarCallState.Held);
        var outgoing = Outgoing(registry);

        Assert.Throws<InvalidOperationException>(() => registry.ConnectOutgoing(incoming.Id));
        Assert.Equal(CarCallState.Ringing, registry.Find(incoming.Id)!.State);
        Assert.Throws<InvalidOperationException>(() => registry.ConnectOutgoing(held.Id));
        Assert.Equal(CarCallState.Active, registry.ConnectOutgoing(outgoing.Id)!.State);
        Assert.Throws<InvalidOperationException>(() => registry.ConnectOutgoing(outgoing.Id));
        registry.Transition(outgoing.Id, CarCallState.Ended);
        Assert.Null(registry.ConnectOutgoing(outgoing.Id));
    }
}
