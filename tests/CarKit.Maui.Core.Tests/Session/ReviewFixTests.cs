using CarKit.Maui.Core.Tests.Validation;
using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Session;

// Tests from the final review of Stage 1: each reproduces one problem that was found
public class ReviewFixTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parallel_pushes_respect_depth_and_host_order()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Fueling);
        host.CallDelay = TimeSpan.FromMilliseconds(20);
        CarListTemplate[] pushed = [new("A"), new("B"), new("C")];

        var results = await Task.WhenAll(pushed.Select(async t =>
        {
            try
            {
                await session.PushAsync(t);
                return null;
            }
            catch (CarTemplateLimitException ex)
            {
                return ex;
            }
        })).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Single(results, r => r is not null);
        Assert.Equal(3, session.Stack.Count);
        var hostOrder = host.Calls.Where(c => c.Operation == FakeCarHostOperation.Push).Select(c => c.Template!.Id);
        Assert.Equal(session.Stack.Skip(1).Select(t => t.Id), hostOrder);
    }

    [Fact]
    public async Task Events_are_raised_after_the_gate_is_released()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        bool? innerCompletedSynchronously = null;
        session.TemplateAppeared += (_, e) =>
        {
            if (innerCompletedSynchronously is null && e.Template.Title?.Text == "First")
            {
                innerCompletedSynchronously = session.PushAsync(new CarListTemplate("Second")).IsCompleted;
            }
        };

        await session.PushAsync(new CarListTemplate("First")).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.True(innerCompletedSynchronously);
    }

    [Fact]
    public async Task Dispatcher_detects_thread_hops_and_serializes_work()
    {
        using var dispatcher = new FakeCarDispatcher();
        var hopped = await dispatcher.DispatchAsync(async () =>
        {
            await Task.Delay(1, Ct).ConfigureAwait(false);
            return dispatcher.IsDispatchRequired;
        });
        var stayed = await dispatcher.DispatchAsync(async () =>
        {
            await Task.Delay(1, Ct);
            return dispatcher.IsDispatchRequired;
        });
        var active = 0;
        var overlap = false;
        Task Work() => dispatcher.DispatchAsync(() =>
        {
            overlap |= Interlocked.Increment(ref active) > 1;
            Thread.Sleep(20);
            Interlocked.Decrement(ref active);
            return Task.CompletedTask;
        });

        await Task.WhenAll(Task.Run(Work, Ct), Task.Run(Work, Ct)).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.True(hopped);
        Assert.False(stayed);
        Assert.False(overlap);
    }

    [Fact]
    public async Task Back_pressed_while_push_is_in_flight_pops_the_right_template()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        var a = new CarListTemplate("A");
        var b = new CarListTemplate("B");
        await session.PushAsync(a);
        host.CallDelay = TimeSpan.FromMilliseconds(50);

        var push = session.PushAsync(b);
        await host.PressBackAsync();
        await push.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(["root", b.Id], session.Stack.Select(t => t.Id));
    }

    [Fact]
    public async Task Same_id_on_stack_and_modal_does_not_corrupt_bookkeeping()
    {
        var (_, session) = await S.ConnectWithRoot();
        var info = new CarInformationTemplate("Station") { Id = "st-1" };
        await session.PushAsync(info);
        await session.PresentAsync(new CarAlertTemplate("Station") { Id = "st-1" });
        await session.DismissAsync();

        await session.UpdateAsync(info with { Title = "Station 2" });
        await session.PopToAsync(info);

        Assert.Equal("Station 2", session.Stack[^1].Title?.Text);
    }

    [Fact]
    public async Task Different_elements_with_the_same_id_are_rejected()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        var prototype = new CarListItem("x");
        string[] names = ["Fuelco", "Shell"];
        var shared = new CarAction("Navigate");
        var list = new CarListTemplate("Stations")
        {
            Sections = [new CarSection(names.Select(n => prototype with { Text = n }))],
        };
        var info = new CarInformationTemplate("I") { Actions = [shared, shared] };

        var ex = await Assert.ThrowsAsync<CarKitException>(() => session.PushAsync(list));
        Assert.Contains(prototype.Id, ex.Message, StringComparison.Ordinal);
        var (_, communication) = await S.ConnectWithRoot();
        await communication.PushAsync(info);
    }

    [Fact]
    public async Task Fake_host_simulates_what_the_platform_shows()
    {
        var items = Enumerable.Range(0, 20).Select(i => new CarListItem($"{i}")).ToArray();
        var (host, _) = await S.ConnectWithRoot(CarAppCategory.Audio, root: new CarListTemplate("L") { Sections = [new CarSection(items)] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectAsync(items[19]));
        await host.SelectAsync(items[11]);

        var selected = false;
        var call = new CarAction("Call", _ => { selected = true; return Task.CompletedTask; });
        var contact = new CarPlayContactTemplate("Anna").WithFallback(new CarInformationTemplate("Anna") { Actions = [call] });
        var (androidHost, android) = await S.ConnectWithRoot(CarAppCategory.Communication, CarPlatform.AndroidAuto);
        await android.PushAsync(contact);
        await androidHost.SelectAsync(call);
        Assert.True(selected);
    }

    [Fact]
    public async Task Throwing_token_callback_does_not_abort_disconnect()
    {
        var handler = new TestHandler((s, ct) =>
        {
            ct.Register(() => throw new InvalidOperationException("callback"));
            return s.SetRootAsync(new CarListTemplate("A"));
        });
        var host = new FakeCarHost(CarAppCategory.Audio);
        var session = await host.ConnectAsync(handler);
        var disconnected = false;
        session.Disconnected += (_, _) => disconnected = true;

        await host.DisconnectAsync();

        Assert.True(disconnected);
        Assert.Equal(1, handler.DisconnectCount);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Operation_after_disconnect_inside_handler_is_not_an_error()
    {
        var gate = new TaskCompletionSource();
        var reached = new TaskCompletionSource();
        var host = new FakeCarHost(CarAppCategory.Audio);
        var connect = host.ConnectAsync(new TestHandler(async (s, _) =>
        {
            reached.SetResult();
            await gate.Task;
            await s.SetRootAsync(new CarListTemplate("Too late"));
        }));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        await host.DisconnectAsync();
        gate.SetResult();
        await connect.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Queued_operation_fails_fast_after_disconnect()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        host.CallDelay = Timeout.InfiniteTimeSpan;
        _ = session.PushAsync(new CarListTemplate("Hangs"));
        var queued = session.PushAsync(new CarListTemplate("Queued"));

        await host.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5), Ct);

        await Assert.ThrowsAsync<CarNotConnectedException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5), Ct));
    }

    [Fact]
    public void Off_mode_still_resolves_tab_children()
    {
        var validator = V.Create(CarAppCategory.Communication, configure: o => o.Validation = CarValidationMode.Off);

        var tabs = Assert.IsType<CarTabTemplate>(validator.Prepare(new CarTabTemplate([new CarLongMessageTemplate("R", "Content")]), 1));

        Assert.IsType<CarInformationTemplate>(Assert.Single(tabs.Tabs));
    }

    [Fact]
    public void Message_on_carplay_needs_information_support()
    {
        var message = new CarMessageTemplate("No connection");

        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Audio).Prepare(message, 1));
        Assert.Throws<CarTemplateNotSupportedException>(() => V.Create(CarAppCategory.Parking, ios: new Version(13, 0)).Prepare(message, 1));
        Assert.Same(message, V.Create(CarAppCategory.Parking).Prepare(message, 1));
    }

    [Fact]
    public async Task Modal_shown_as_screen_on_android_counts_toward_depth()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Iot, CarPlatform.AndroidAuto);
        for (var i = 0; i < 4; i++)
        {
            await session.PushAsync(new CarListTemplate($"{i}"));
        }

        await Assert.ThrowsAsync<CarTemplateLimitException>(() => session.PresentAsync(new CarActionSheetTemplate("A", "B")));
        await session.PresentAsync(new CarAlertTemplate("Alert stays an overlay"));
    }
}
