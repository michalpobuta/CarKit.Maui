using CarKit.Maui.Communication;
using CarKit.Maui.Tests.Media;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarKit.Maui.Tests.Communication;

// Replies and read marks from notification actions and Siri → ICarMessageHandler on the main thread (Review Focus 1)
public sealed class MessageRouterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carkit-router-" + Guid.NewGuid().ToString("N"));
    private readonly TestMessageHandler _handler = new();
    private readonly RecordingErrors _errors = new();
    private readonly ManualDispatcher _dispatcher = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private CarMessageRouter Router(bool withHandler = true)
    {
        var services = new ServiceCollection();
        if (withHandler)
        {
            services.AddSingleton<ICarMessageHandler>(_handler);
        }

        return new CarMessageRouter(services.BuildServiceProvider(), _errors, _dispatcher);
    }

    [Fact]
    public async Task Reply_reaches_the_handler_on_the_main_thread()
    {
        var router = Router();
        _dispatcher.OnMainThread = false;

        Assert.True(await router.ReplyAsync("anna", "On my way"));
        Assert.Empty(_handler.Calls);
        _dispatcher.Pump();

        Assert.Equal(["reply:anna:On my way"], _handler.Calls);
        Assert.Empty(_errors.Errors);
    }

    [Fact]
    public async Task Mark_as_read_reaches_the_handler()
    {
        await Router().MarkAsReadAsync("team");

        Assert.Equal(["read:team"], _handler.Calls);
    }

    [Fact]
    public async Task Reply_without_handler_reports_an_error()
    {
        Assert.False(await Router(withHandler: false).ReplyAsync("anna", "x"));

        var (exception, context) = Assert.Single(_errors.Errors);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("UseMessageHandler", exception.Message, StringComparison.Ordinal);
        Assert.Equal("Communication.Reply", context.Operation);
    }

    [Fact]
    public async Task Handler_exception_goes_to_the_error_handler()
    {
        _handler.Throw = new TimeoutException("server");

        // Review M2: a failed reply does not reach the notification as sent
        Assert.False(await Router().ReplyAsync("anna", "x"));

        var (exception, context) = Assert.Single(_errors.Errors);
        Assert.IsType<TimeoutException>(exception);
        Assert.Equal(CarMessageRouter.ReplyOperation, context.Operation);
    }

    [Fact]
    public async Task Drain_routes_store_requests_in_order()
    {
        var store = new CarConversationStore(_directory);
        await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, "anna") { Text = "In 5 minutes" }, TestContext.Current.CancellationToken);
        await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.MarkAsRead, "team"), TestContext.Current.CancellationToken);
        await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, "team"), TestContext.Current.CancellationToken);

        await Router().DrainAsync(store);
        await Router().DrainAsync(store);

        Assert.Equal(["reply:anna:In 5 minutes", "read:team", "reply:team:"], _handler.Calls);
        Assert.Empty(_errors.Errors);
    }
}
