using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Session;

// Stage 3: selecting a modal action (alert, sheet) closes it before the handler: FakeCarHost reproduces the renderers' order
public class ModalActionTests
{
    [Fact]
    public async Task Selecting_an_alert_action_dismisses_before_the_handler()
    {
        var (host, session) = await S.ConnectWithRoot();
        var events = S.RecordEvents(session);
        CarTemplate? presentedInHandler = null;
        var second = new CarAlertTemplate("Second") { Id = "second" };
        var confirm = new CarAction("Reserve", async context =>
        {
            presentedInHandler = context.Session.Presented;
            await context.Session.DismissAsync();
            await context.Session.PresentAsync(second);
        });
        await session.PresentAsync(new CarAlertTemplate("First") { Id = "first", Actions = [confirm] });
        events.Clear();

        await host.SelectAsync(confirm);

        Assert.Null(presentedInHandler);
        Assert.Same(second, session.Presented);
        Assert.Equal(["-first", "+second"], events);
        Assert.DoesNotContain(host.Calls, c => c.Operation == FakeCarHostOperation.Dismiss);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Selecting_an_action_sheet_action_dismisses_it()
    {
        var (host, session) = await S.ConnectWithRoot();
        var ran = false;
        var delete = new CarAction("Delete", _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await session.PresentAsync(new CarActionSheetTemplate("Title", "Are you sure?") { Actions = [delete] });

        await host.SelectAsync(delete);

        Assert.True(ran);
        Assert.Null(session.Presented);
    }

    [Fact]
    public async Task Selecting_a_pushed_message_action_keeps_the_screen()
    {
        var (host, session) = await S.ConnectWithRoot();
        var ok = new CarAction("OK");
        var message = new CarMessageTemplate("Done") { Id = "message", Actions = [ok] };
        await session.PushAsync(message);

        await host.SelectAsync(ok);

        Assert.Equal("message", session.Stack[^1].Id);
        Assert.Equal(2, session.Stack.Count);
    }
}

// I6 (review): the Android Auto host closes the modal screen during its own stack operation (push, new root);
// the session learns about it synchronously, so the next PresentAsync works right away
public class HostClosedModalTests
{
    private sealed class ClosingHost : ICarHost
    {
        public CarSession? Session { get; set; }

        public string? PresentedId { get; private set; }

        public CarPlatform Platform => CarPlatform.AndroidAuto;

        public ICarCapabilities Capabilities { get; } = new CarCapabilities(CarPlatform.AndroidAuto, null, 8);

        public CarContentStyle ContentStyle => CarContentStyle.Light;

        public Task SetRootAsync(CarTemplate root, bool animated) => Task.CompletedTask;

        public Task PushAsync(CarTemplate template, bool animated)
        {
            if (PresentedId is { } id)
            {
                PresentedId = null;
                Session!.OnHostClosedModalDuringOperation(id);
            }

            return Task.CompletedTask;
        }

        public Task PopAsync(bool animated) => Task.CompletedTask;

        public Task PopToAsync(CarTemplate template, bool animated) => Task.CompletedTask;

        public Task PopToRootAsync(bool animated) => Task.CompletedTask;

        public Task PresentAsync(CarTemplate modal, bool animated)
        {
            PresentedId = modal.Id;
            return Task.CompletedTask;
        }

        public Task DismissAsync(bool animated) => Task.CompletedTask;

        public Task UpdateAsync(CarTemplate previous, CarTemplate updated) => Task.CompletedTask;

        public TNative? GetNative<TNative>() where TNative : class => null;
    }

    [Fact]
    public async Task Modal_closed_by_a_host_operation_is_cleared_before_the_next_call()
    {
        using var dispatcher = new FakeCarDispatcher();
        var host = new ClosingHost();
        using var session = new CarSession(host, new CarKitOptions { Categories = CarAppCategory.Parking, Validation = CarValidationMode.Strict }, dispatcher, new ThrowingErrors(), new TestLogger());
        host.Session = session;
        var events = S.RecordEvents(session);
        await session.SetRootAsync(new CarListTemplate("Menu") { Id = "menu" });
        await session.PresentAsync(new CarActionSheetTemplate("Sheet", null) { Id = "sheet" });

        await session.PushAsync(new CarListTemplate("Details") { Id = "details" });
        await session.PresentAsync(new CarAlertTemplate("Another") { Id = "alert" });

        Assert.Equal("alert", session.Presented?.Id);
        Assert.Equal(["+menu", "+sheet", "-sheet", "-menu", "+details", "+alert"], events);
    }

    [Fact]
    public async Task Closing_an_unknown_modal_changes_nothing()
    {
        using var dispatcher = new FakeCarDispatcher();
        var host = new ClosingHost();
        using var session = new CarSession(host, new CarKitOptions { Categories = CarAppCategory.Parking }, dispatcher, new ThrowingErrors(), new TestLogger());
        await session.SetRootAsync(new CarListTemplate("Menu"));
        await session.PresentAsync(new CarAlertTemplate("A") { Id = "a" });

        session.OnHostClosedModalDuringOperation("other");

        Assert.Equal("a", session.Presented?.Id);
    }

    private sealed class ThrowingErrors : ICarErrorHandler
    {
        public void HandleError(Exception exception, CarErrorContext context) => throw new InvalidOperationException("Unexpected callback error.", exception);
    }
}
