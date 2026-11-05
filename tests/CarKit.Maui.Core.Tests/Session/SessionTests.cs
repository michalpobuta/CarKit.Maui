using CarKit.Maui.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarKit.Maui.Core.Tests.Session;

internal sealed class TestHandler(Func<ICarSession, CancellationToken, Task> onConnected) : ICarAppHandler
{
    public int DisconnectCount { get; private set; }

    public Task OnConnectedAsync(ICarSession session, CancellationToken ct) => onConnected(session, ct);

    public Task OnDisconnectedAsync(ICarSession session)
    {
        DisconnectCount++;
        return Task.CompletedTask;
    }
}

internal static class S
{
    public static async Task<(FakeCarHost Host, ICarSession Session)> ConnectWithRoot(
        CarAppCategory categories = CarAppCategory.Communication, CarPlatform platform = CarPlatform.CarPlay, CarTemplate? root = null)
    {
        var host = new FakeCarHost(categories, platform);
        var session = await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(root ?? new CarListTemplate("Start") { Id = "root" })));
        return (host, session);
    }

    public static List<string> RecordEvents(ICarSession session)
    {
        var events = new List<string>();
        session.TemplateAppeared += (_, e) => events.Add("+" + e.Template.Id);
        session.TemplateDisappeared += (_, e) => events.Add("-" + e.Template.Id);
        return events;
    }
}

public class ConnectionTests
{
    [Fact]
    public async Task Connect_runs_handler_and_sets_root()
    {
        var (host, session) = await S.ConnectWithRoot();

        Assert.True(session.IsConnected);
        Assert.Same(session, host.Session);
        Assert.Equal(CarPlatform.CarPlay, session.Platform);
        Assert.Equal(new Version(26, 4), session.Capabilities.IosVersion);
        Assert.Equal(CarContentStyle.Light, session.ContentStyle);
        Assert.Equal("root", Assert.Single(session.Stack).Id);
        var call = Assert.Single(host.Calls);
        Assert.Equal(FakeCarHostOperation.SetRoot, call.Operation);
        Assert.False(call.Animated);
    }

    [Fact]
    public async Task Android_session_reports_api_level()
    {
        var (_, session) = await S.ConnectWithRoot(platform: CarPlatform.AndroidAuto);

        Assert.Equal(8, session.Capabilities.CarAppApiLevel);
        Assert.Null(session.Capabilities.IosVersion);
    }

    [Fact]
    public async Task Disconnect_keeps_stack_and_rejects_operations()
    {
        var handler = new TestHandler((s, _) => s.SetRootAsync(new CarListTemplate("Start")));
        var host = new FakeCarHost(CarAppCategory.Audio);
        var session = await host.ConnectAsync(handler);
        var disconnected = 0;
        session.Disconnected += (_, _) => disconnected++;

        await host.DisconnectAsync();

        Assert.False(session.IsConnected);
        Assert.Equal(1, disconnected);
        Assert.Equal(1, handler.DisconnectCount);
        Assert.Single(session.Stack);
        await Assert.ThrowsAsync<CarNotConnectedException>(() => session.PushAsync(new CarGridTemplate("G")));
        await Assert.ThrowsAsync<CarNotConnectedException>(() => session.SetRootAsync(new CarGridTemplate("G")));
        await Assert.ThrowsAsync<CarNotConnectedException>(() => session.DismissAsync());
    }

    [Fact]
    public async Task Disconnect_during_loading_cancels_the_handler()
    {
        var reachedLoading = new TaskCompletionSource();
        CancellationToken observed = default;
        var host = new FakeCarHost(CarAppCategory.Audio);
        var connect = host.ConnectAsync(new TestHandler(async (s, ct) =>
        {
            observed = ct;
            await s.SetRootAsync(new CarListTemplate("Loading") { IsLoading = true });
            reachedLoading.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            await s.PushAsync(new CarGridTemplate("Never"));
        }));
        await reachedLoading.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await host.DisconnectAsync();
        var session = await connect.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(observed.IsCancellationRequested);
        Assert.False(session.IsConnected);
        Assert.Equal("Loading", Assert.Single(session.Stack).Title?.Text);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Handler_failure_on_connect_goes_to_error_handler()
    {
        var host = new FakeCarHost(CarAppCategory.Audio);

        var session = await host.ConnectAsync(new TestHandler((_, _) => throw new InvalidOperationException("boom")));

        Assert.True(session.IsConnected);
        Assert.IsType<InvalidOperationException>(Assert.Single(host.Errors));
    }

    [Fact]
    public async Task Fake_host_must_be_connected_for_simulations()
    {
        var host = new FakeCarHost(CarAppCategory.Audio);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.PressBackAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.DisconnectAsync());
        await Assert.ThrowsAsync<ArgumentNullException>(() => host.ConnectAsync(null!));
    }

    [Fact]
    public async Task Content_style_change_raises_event()
    {
        var (host, session) = await S.ConnectWithRoot();
        CarContentStyle? received = null;
        session.ContentStyleChanged += (_, style) => received = style;

        await host.ChangeContentStyleAsync(CarContentStyle.Dark);

        Assert.Equal(CarContentStyle.Dark, received);
        Assert.Equal(CarContentStyle.Dark, session.ContentStyle);
        Assert.Equal(CarContentStyle.Dark, host.ContentStyle);
    }

    [Fact]
    public async Task GetNative_returns_the_fake_host()
    {
        var (host, session) = await S.ConnectWithRoot();

        Assert.Same(host, session.GetNative<FakeCarHost>());
        Assert.Null(session.GetNative<string>());
    }
}

public class StackTests
{
    [Fact]
    public async Task Push_and_pop_update_stack_calls_and_events()
    {
        var (host, session) = await S.ConnectWithRoot();
        var events = S.RecordEvents(session);
        var info = new CarInformationTemplate("Info") { Id = "info" };

        await session.PushAsync(info);
        await session.PopAsync(animated: false);

        Assert.Equal(["-root", "+info", "-info", "+root"], events);
        Assert.Equal("root", Assert.Single(session.Stack).Id);
        Assert.Equal([FakeCarHostOperation.SetRoot, FakeCarHostOperation.Push, FakeCarHostOperation.Pop], host.Calls.Select(c => c.Operation));
        Assert.False(host.Calls[^1].Animated);
    }

    [Fact]
    public async Task PopTo_and_PopToRoot_remove_templates_above()
    {
        var (host, session) = await S.ConnectWithRoot();
        var a = new CarListTemplate("A");
        var b = new CarListTemplate("B");
        var c = new CarListTemplate("C");
        await session.PushAsync(a);
        await session.PushAsync(b);
        await session.PushAsync(c);

        await session.PopToAsync(a);
        Assert.Equal(["root", a.Id], session.Stack.Select(t => t.Id));
        Assert.Same(a, host.Calls[^1].Template);

        await session.PopToAsync(a);
        await session.PopToRootAsync();
        await session.PopToRootAsync();

        Assert.Single(session.Stack);
        Assert.Equal(1, host.Calls.Count(call => call.Operation == FakeCarHostOperation.PopToRoot));
        Assert.Equal(1, host.Calls.Count(call => call.Operation == FakeCarHostOperation.PopTo));
    }

    [Fact]
    public async Task SetRoot_replaces_the_stack()
    {
        var (_, session) = await S.ConnectWithRoot();
        await session.PushAsync(new CarListTemplate("A"));
        var events = S.RecordEvents(session);
        var grid = new CarGridTemplate("New") { Id = "grid" };

        await session.SetRootAsync(grid);

        Assert.Same(grid, Assert.Single(session.Stack));
        Assert.Equal(2, events.Count);
        Assert.Equal("+grid", events[1]);
    }

    [Fact]
    public async Task Present_and_dismiss_modal()
    {
        var (host, session) = await S.ConnectWithRoot();
        var events = S.RecordEvents(session);
        var alert = new CarAlertTemplate("Attention") { Id = "alert" };

        await session.PresentAsync(alert);
        Assert.Same(alert, session.Presented);
        await Assert.ThrowsAsync<CarStackException>(() => session.PresentAsync(new CarAlertTemplate("Second")));

        await session.DismissAsync();
        await session.DismissAsync();

        Assert.Null(session.Presented);
        Assert.Equal(["+alert", "-alert"], events);
        Assert.Equal(1, host.Calls.Count(call => call.Operation == FakeCarHostOperation.Dismiss));
    }

    [Fact]
    public async Task Update_replaces_template_with_same_id()
    {
        var (host, session) = await S.ConnectWithRoot();
        var root = (CarListTemplate)session.Stack[0];
        var updated = root with { Sections = [new CarSection([new CarListItem("New")])] };

        await session.UpdateAsync(updated);

        Assert.Same(updated, session.Stack[0]);
        Assert.Equal(FakeCarHostOperation.Update, host.Calls[^1].Operation);
    }

    [Fact]
    public async Task Update_works_for_presented_modal()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Navigation);
        var voice = new CarVoiceControlTemplate([new CarVoiceControlState("listen", "Listening"), new CarVoiceControlState("think", "Thinking")]);
        await session.PresentAsync(voice);

        await session.UpdateAsync(voice with { ActiveStateId = "think" });

        Assert.Equal("think", ((CarVoiceControlTemplate)session.Presented!).ActiveStateId);
    }

    [Fact]
    public async Task Update_does_not_consume_a_depth_step()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Fueling);
        await session.PushAsync(new CarListTemplate("2"));
        var top = new CarListTemplate("3");
        await session.PushAsync(top);

        for (var i = 0; i < 5; i++)
        {
            await session.UpdateAsync(top with { Title = $"3.{i}" });
        }

        Assert.Equal(3, session.Stack.Count);
        await Assert.ThrowsAsync<CarTemplateLimitException>(() => session.PushAsync(new CarListTemplate("4")));
        Assert.Equal(3, session.Stack.Count);
    }

    [Fact]
    public async Task Invalid_stack_operations_throw()
    {
        var (_, session) = await S.ConnectWithRoot();
        var a = new CarListTemplate("A");
        await session.PushAsync(a);

        await Assert.ThrowsAsync<CarStackException>(() => session.PushAsync(new CarAlertTemplate("Modal")));
        await Assert.ThrowsAsync<CarStackException>(() => session.PushAsync(a));
        await Assert.ThrowsAsync<CarStackException>(() => session.PopToAsync(new CarListTemplate("Foreign")));
        await Assert.ThrowsAsync<CarStackException>(() => session.SetRootAsync(new CarAlertTemplate("Modal")));
        await Assert.ThrowsAsync<CarStackException>(() => session.UpdateAsync(new CarListTemplate("Unknown")));
        await Assert.ThrowsAsync<CarStackException>(() => session.UpdateAsync(new CarGridTemplate("Other type") { Id = a.Id }));
        await session.PopAsync();
        await Assert.ThrowsAsync<CarStackException>(() => session.PopAsync());
        await Assert.ThrowsAsync<ArgumentNullException>(() => session.PushAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => session.PresentAsync(null!));
    }

    [Fact]
    public async Task Operations_before_root_throw()
    {
        var host = new FakeCarHost(CarAppCategory.Audio);
        var session = await host.ConnectAsync(new TestHandler((_, _) => Task.CompletedTask));

        await Assert.ThrowsAsync<CarStackException>(() => session.PushAsync(new CarListTemplate("A")));
        await Assert.ThrowsAsync<CarStackException>(() => session.PopToRootAsync());
        await Assert.ThrowsAsync<CarStackException>(() => session.PopAsync());
    }

    [Fact]
    public async Task Validation_failure_leaves_stack_unchanged()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Parking);

        await Assert.ThrowsAsync<CarTemplateNotAllowedException>(() => session.PushAsync(new CarNowPlayingTemplate()));

        Assert.Single(session.Stack);
        Assert.Single(host.Calls);
    }

    [Fact]
    public async Task Native_failure_is_wrapped_and_stack_unchanged()
    {
        var (host, session) = await S.ConnectWithRoot();
        var native = new InvalidOperationException("CPInterfaceController");
        host.FailNextCall(native);

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => session.PushAsync(new CarListTemplate("A")));

        Assert.Same(native, ex.InnerException);
        Assert.Single(session.Stack);
        await session.PushAsync(new CarListTemplate("B"));
        Assert.Equal(2, session.Stack.Count);
    }

    [Fact]
    public async Task Host_receives_fallback_while_stack_keeps_original()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Iot, CarPlatform.AndroidAuto);
        var sheet = new CarActionSheetTemplate("Gate", "Open it?");

        await session.PresentAsync(sheet);

        Assert.Same(sheet, session.Presented);
        var sent = Assert.IsType<CarMessageTemplate>(host.Calls[^1].Template);
        Assert.Equal(sheet.Id, sent.Id);
        await session.UpdateAsync(sheet with { Message = "Are you sure?" });
        Assert.Equal("Are you sure?", Assert.IsType<CarMessageTemplate>(host.Calls[^1].Template).Message.Text);
    }

    [Fact]
    public async Task Truncated_template_reaches_host_but_stack_keeps_original()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        host.SetLimit(CarLimitKind.ListItems, 2);
        var list = new CarListTemplate("Many") { Sections = [new CarSection(Enumerable.Range(0, 5).Select(i => new CarListItem($"{i}")))] };

        await session.PushAsync(list);

        Assert.Same(list, session.Stack[^1]);
        Assert.Equal(2, Assert.IsType<CarListTemplate>(host.Calls[^1].Template).Sections[0].Items.Count);
    }
}

public class HostCallbackTests
{
    [Fact]
    public async Task Back_button_pops_and_raises_events()
    {
        var (host, session) = await S.ConnectWithRoot();
        await session.PushAsync(new CarListTemplate("A") { Id = "a" });
        var events = S.RecordEvents(session);

        await host.PressBackAsync();
        await host.PressBackAsync();

        Assert.Single(session.Stack);
        Assert.Equal(["-a", "+root"], events);
    }

    [Fact]
    public async Task Host_dismiss_clears_presented()
    {
        var (host, session) = await S.ConnectWithRoot();
        await session.PresentAsync(new CarAlertTemplate("A"));

        await host.DismissPresentedAsync();
        await host.DismissPresentedAsync();

        Assert.Null(session.Presented);
    }
}

public class ThreadingTests
{
    [Fact]
    public async Task Native_calls_handlers_and_events_run_on_main_thread()
    {
        var onMain = new List<bool>();
        var (host, session) = await S.ConnectWithRoot();
        session.TemplateAppeared += (_, _) => onMain.Add(!host.Dispatcher.IsDispatchRequired);
        var item = new CarListItem("A") { OnSelected = _ => { onMain.Add(!host.Dispatcher.IsDispatchRequired); return Task.CompletedTask; } };

        await session.PushAsync(new CarListTemplate("L") { Sections = [new CarSection([item])] });
        await host.SelectAsync(item);

        Assert.All(host.Calls, call => Assert.True(call.OnMainThread));
        Assert.Equal([true, true], onMain);
        Assert.True(host.Dispatcher.DispatchCount > 0);
        Assert.True(host.Dispatcher.IsDispatchRequired);
    }

    [Fact]
    public async Task Parallel_pushes_are_serialized()
    {
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        host.CallDelay = TimeSpan.FromMilliseconds(20);
        var a = new CarListTemplate("A");
        var b = new CarListTemplate("B");

        await Task.WhenAll(session.PushAsync(a), session.PushAsync(b)).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(3, session.Stack.Count);
        Assert.Equal(3, session.Stack.Select(t => t.Id).Distinct().Count());
        Assert.Equal(2, host.Calls.Count(call => call.Operation == FakeCarHostOperation.Push));
    }

    [Fact]
    public async Task Event_handler_can_push_without_deadlock()
    {
        var (_, session) = await S.ConnectWithRoot(CarAppCategory.Audio);
        var second = new CarListTemplate("Second");
        var pushed = false;
        session.TemplateAppeared += async (_, e) =>
        {
            if (!pushed && e.Template.Id != second.Id)
            {
                pushed = true;
                await session.PushAsync(second);
            }
        };

        await session.PushAsync(new CarListTemplate("First")).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Run(
            async () =>
            {
                while (session.Stack.Count < 3)
                {
                    await Task.Yield();
                }
            },
            TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(second, session.Stack[^1]);
    }
}

public class SimulationTests
{
    [Fact]
    public async Task Handler_exception_is_reported_and_session_survives()
    {
        var item = new CarListItem("Bad") { OnSelected = _ => throw new InvalidOperationException("handler") };
        var (host, session) = await S.ConnectWithRoot(root: new CarListTemplate("L") { Sections = [new CarSection([item])] });

        await host.SelectAsync(item);
        await session.PushAsync(new CarListTemplate("Next"));

        Assert.Equal("handler", Assert.Single(host.Errors).Message);
        Assert.Equal(2, session.Stack.Count);
    }

    [Fact]
    public async Task Select_invokes_handlers_of_every_element_type()
    {
        var selected = new List<string>();
        Func<CarItemContext, Task> record = ctx =>
        {
            selected.Add(ctx.Element.Id);
            Assert.False(ctx.CancellationToken.IsCancellationRequested);
            return Task.CompletedTask;
        };
        var action = new CarAction("Go", record) { Id = "action" };
        var bar = new CarBarButton("Search") { Id = "bar", OnSelected = record };
        var info = new CarInformationTemplate("I") { Actions = [action], TrailingBarButtons = [bar] };
        var (host, session) = await S.ConnectWithRoot(root: info);
        var grid = new CarGridItem("G", CarImage.FromFile("g.png")) { Id = "grid", OnSelected = record };
        var noHandler = new CarGridItem("Without", CarImage.FromFile("g.png"));

        await host.SelectAsync(action);
        await host.SelectAsync(bar);
        await session.PushAsync(new CarGridTemplate("G") { Items = [grid, noHandler] });
        await host.SelectAsync(grid);
        await host.SelectAsync(noHandler);

        Assert.Equal(["action", "bar", "grid"], selected);
    }

    [Fact]
    public async Task Select_covers_points_map_now_playing_and_contact_buttons()
    {
        var selected = new List<string>();
        Func<CarItemContext, Task> record = ctx => { selected.Add(ctx.Element.Id); return Task.CompletedTask; };
        var primary = new CarAction("Navigate", record) { Id = "primary" };
        var point = new CarPointOfInterest(new CarLocation(52, 21), "Station") { Id = "point", OnSelected = record, PrimaryAction = primary };
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Parking, root: new CarPointOfInterestTemplate("P") { Points = [point] });

        await host.SelectAsync(point);
        await host.SelectAsync(primary);

        var navHost = new FakeCarHost(CarAppCategory.Navigation | CarAppCategory.Audio | CarAppCategory.Communication);
        var mapButton = new CarMapButton(CarImage.FromFile("z.png")) { Id = "map", OnSelected = record };
        var navSession = await navHost.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(new CarMapTemplate { MapButtons = [mapButton] })));
        await navHost.SelectAsync(mapButton);
        var shuffle = new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle) { Id = "shuffle", OnSelected = record };
        await navSession.PushAsync(new CarNowPlayingTemplate { Buttons = [shuffle] });
        await navHost.SelectAsync(shuffle);
        var call = new CarPlayContactButton(CarPlayContactButtonKind.Call) { Id = "call", OnSelected = record };
        await navSession.PushAsync(new CarPlayContactTemplate("Anna") { Buttons = [call] });
        await navHost.SelectAsync(call);

        Assert.Equal(["point", "primary", "map", "shuffle", "call"], selected);
    }

    [Fact]
    public async Task Select_in_presented_modal_and_tabs()
    {
        var selected = new List<string>();
        Func<CarItemContext, Task> record = ctx => { selected.Add(ctx.Element.Id); return Task.CompletedTask; };
        var tabItem = new CarListItem("In a tab") { Id = "tab-item", OnSelected = record };
        var tabs = new CarTabTemplate([new CarListTemplate("A") { Sections = [new CarSection([tabItem])] }]);
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Audio, root: tabs);
        var ok = new CarAction("OK", record) { Id = "ok" };

        await host.SelectAsync(tabItem);
        await session.PresentAsync(new CarAlertTemplate("A") { Actions = [ok] });
        await host.SelectAsync(ok);

        Assert.Equal(["tab-item", "ok"], selected);
    }

    [Fact]
    public async Task Selecting_hidden_or_unselectable_elements_fails()
    {
        var hidden = new CarListItem("Hidden");
        var section = new CarSection([new CarListItem("A")]);
        var (host, _) = await S.ConnectWithRoot(root: new CarListTemplate("L") { Sections = [section] });

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectAsync(hidden));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectAsync(section));
        await Assert.ThrowsAsync<ArgumentNullException>(() => host.SelectAsync(null!));
    }

    [Fact]
    public async Task Toggle_and_image_row_pass_their_values()
    {
        bool? toggled = null;
        int? image = null;
        var toggleItem = new CarListItem("Wi-Fi") { Toggle = new CarListItemToggle(false) { OnToggled = ctx => { toggled = ctx.IsChecked; return Task.CompletedTask; } } };
        var imageRow = new CarListImageRowItem("Recent", [CarImage.FromFile("a.png"), CarImage.FromFile("b.png")])
        {
            OnImageSelected = ctx => { image = ctx.ImageIndex; return Task.CompletedTask; },
        };
        var (host, _) = await S.ConnectWithRoot(CarAppCategory.Iot, CarPlatform.AndroidAuto,
            new CarListTemplate("L") { Sections = [new CarSection([toggleItem, imageRow])] });

        await host.ToggleAsync(toggleItem, isChecked: true);
        await host.SelectImageAsync(imageRow, 1);

        Assert.True(toggled);
        Assert.Equal(1, image);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.SelectImageAsync(imageRow, 5));
    }

    [Fact]
    public async Task Search_returns_results_and_submits()
    {
        string? submitted = null;
        var search = new CarSearchTemplate
        {
            OnSearchTextChanged = ctx => Task.FromResult<IReadOnlyList<CarListItem>>([new CarListItem(ctx.Text.ToUpperInvariant())]),
            OnSearchSubmitted = ctx => { submitted = ctx.Text; return Task.CompletedTask; },
        };
        var (host, _) = await S.ConnectWithRoot(CarAppCategory.Navigation, root: search);

        var results = await host.SearchAsync("fuelco");
        await host.SubmitSearchAsync("bp");

        Assert.Equal("FUELCO", Assert.Single(results).Text.Text);
        Assert.Equal("bp", submitted);
    }

    [Fact]
    public async Task Search_without_handler_or_with_failing_handler_returns_empty()
    {
        var failing = new CarSearchTemplate { OnSearchTextChanged = _ => throw new InvalidOperationException("search") };
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Navigation, root: failing);

        Assert.Empty(await host.SearchAsync("x"));
        Assert.Single(host.Errors);
        await session.SetRootAsync(new CarSearchTemplate());
        Assert.Empty(await host.SearchAsync("x"));
        await host.SubmitSearchAsync("x");
        await session.SetRootAsync(new CarListTemplate("L"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SearchAsync("x"));
    }

    [Fact]
    public async Task Tab_selection_and_sign_in_input_reach_handlers()
    {
        CarTemplate? selectedTab = null;
        string? input = null;
        var second = new CarGridTemplate("Second");
        var tabs = new CarTabTemplate([new CarListTemplate("First"), second])
        {
            OnTabSelected = ctx => { selectedTab = ctx.SelectedTab; return Task.CompletedTask; },
        };
        var (host, session) = await S.ConnectWithRoot(CarAppCategory.Iot, CarPlatform.AndroidAuto, tabs);

        await host.SelectTabAsync(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectTabAsync(new CarListTemplate("Foreign")));
        var signIn = new AndroidAutoSignInTemplate("Sign in", new AndroidAutoInputSignIn { OnInputSubmitted = ctx => { input = ctx.Text; return Task.CompletedTask; } });
        await session.PushAsync(signIn);
        await host.SubmitInputAsync("jan@example.com");

        Assert.Same(second, selectedTab);
        Assert.Equal("jan@example.com", input);
        await session.SetRootAsync(new AndroidAutoSignInTemplate("PIN", new AndroidAutoPinSignIn("1234")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SubmitInputAsync("x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SelectTabAsync(second));
    }

    [Fact]
    public async Task Native_calls_are_logged_when_enabled()
    {
        var logger = new TestLogger();
        var host = new FakeCarHost(CarAppCategory.Audio) { Logger = logger };
        host.Options.Diagnostics.LogNativeCalls = true;

        await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(new CarListTemplate("A"))));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("SetRoot", StringComparison.Ordinal));
    }
}
