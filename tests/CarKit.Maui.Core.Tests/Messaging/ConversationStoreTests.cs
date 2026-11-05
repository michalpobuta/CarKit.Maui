using CarKit.Maui.Core.Tests.Model;
using Xunit;

namespace CarKit.Maui.Core.Tests.Messaging;

// Stage 6: store shared by the app and the Intents Extension (a directory in the App Group container)
public sealed class ConversationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "carkit-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Conversations_round_trip()
    {
        var store = new CarConversationStore(_directory);
        var group = new CarConversationItem("team", new CarText(["Project team", "Team"]), C.Me,
        [
            new CarConversationMessage("m1", C.Anna, "Meeting at 10", DateTimeOffset.FromUnixTimeSeconds(100)) { IsRead = true },
            new CarConversationMessage("m2", null, "I'll be there", DateTimeOffset.FromUnixTimeSeconds(200)),
        ])
        { IsGroupConversation = true, Image = CarImage.FromFile("team.png"), OnReply = _ => Task.CompletedTask };

        await store.SaveConversationsAsync([group, C.Conversation("anna")], TestContext.Current.CancellationToken);
        var loaded = await new CarConversationStore(_directory).LoadConversationsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["team", "anna"], loaded.Select(c => c.Id));
        var team = loaded[0];
        Assert.Equal(["Project team", "Team"], team.Title.Variants);
        Assert.True(team.IsGroupConversation);
        Assert.Equal(C.Me, team.Self);
        Assert.Null(team.Image);
        Assert.Null(team.OnReply);
        Assert.Equal(group.Messages, team.Messages);
        Assert.Equal("+15550100200", team.Messages[0].Sender!.PhoneOrEmail);
    }

    [Fact]
    public async Task Saving_replaces_the_previous_conversations()
    {
        var store = new CarConversationStore(_directory);
        await store.SaveConversationsAsync([C.Conversation("a"), C.Conversation("b")], TestContext.Current.CancellationToken);

        await store.SaveConversationsAsync([C.Conversation("c")], TestContext.Current.CancellationToken);

        Assert.Equal(["c"], (await store.LoadConversationsAsync(TestContext.Current.CancellationToken)).Select(c => c.Id));
        Assert.Equal(["conversations.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Requests_queue_in_order_and_drain_once()
    {
        var extension = new CarConversationStore(_directory);
        var app = new CarConversationStore(_directory);
        await extension.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, "anna") { Text = "On my way" }, TestContext.Current.CancellationToken);
        await extension.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.MarkAsRead, "team"), TestContext.Current.CancellationToken);

        var first = await app.DequeueAllAsync(TestContext.Current.CancellationToken);
        var second = await app.DequeueAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [new CarMessageRequest(CarMessageRequestKind.Reply, "anna") { Text = "On my way" }, new CarMessageRequest(CarMessageRequestKind.MarkAsRead, "team")],
            first);
        Assert.Empty(second);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "requests")));
    }

    [Fact]
    public async Task Concurrent_drains_return_each_request_once()
    {
        var store = new CarConversationStore(_directory);
        for (var i = 0; i < 20; i++)
        {
            await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, "c" + i) { Text = "t" }, TestContext.Current.CancellationToken);
        }

        var drains = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.DequeueAllAsync(TestContext.Current.CancellationToken))));

        Assert.Equal(20, drains.Sum(d => d.Count));
        Assert.Equal(20, drains.SelectMany(d => d).Select(r => r.ConversationId).Distinct().Count());
    }

    [Fact]
    public async Task Missing_directory_loads_empty()
    {
        var store = new CarConversationStore(_directory);

        Assert.Empty(await store.LoadConversationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await store.DequeueAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(_directory, store.Directory);
    }

    [Fact]
    public async Task Corrupt_files_are_skipped()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "requests"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "conversations.json"), "{ not json", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "requests", "0-bad.json"), "[1,", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "requests", "1-empty.json"), """{ "kind": "Reply" }""", TestContext.Current.CancellationToken);
        var store = new CarConversationStore(_directory);
        await store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.MarkAsRead, "ok"), TestContext.Current.CancellationToken);

        Assert.Empty(await store.LoadConversationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(["ok"], (await store.DequeueAllAsync(TestContext.Current.CancellationToken)).Select(r => r.ConversationId));
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, "requests")));
    }

    [Fact]
    public async Task Incomplete_stored_conversations_are_skipped()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "conversations.json"), """
            { "conversations": [
              { "id": "no-self", "title": [ "x" ] },
              { "id": "ok", "title": [ "Anna" ], "self": { "key": "me", "name": "Me" },
                "messages": [ { "id": "m1", "text": "Hi", "sender": { "key": "", "name": "?" } }, { "text": "no id" } ] }
            ] }
            """, TestContext.Current.CancellationToken);

        var loaded = await new CarConversationStore(_directory).LoadConversationsAsync(TestContext.Current.CancellationToken);

        var ok = Assert.Single(loaded);
        Assert.Equal("ok", ok.Id);
        var message = Assert.Single(ok.Messages);
        Assert.Null(message.Sender);
    }

    [Fact]
    public void App_group_store_and_notification_names()
    {
        Assert.Equal(Path.Combine("/group", "CarKit"), CarConversationStore.ForAppGroupContainer("/group").Directory);
        Assert.Equal("group.com.carkit.sample.carkit.requests", CarConversationStore.GetRequestsNotificationName("group.com.carkit.sample"));
        Assert.Throws<ArgumentException>(() => CarConversationStore.ForAppGroupContainer(" "));
        Assert.Throws<ArgumentException>(() => CarConversationStore.GetRequestsNotificationName(""));
    }

    [Fact]
    public async Task Requests_need_a_conversation()
    {
        var store = new CarConversationStore(_directory);

        await Assert.ThrowsAsync<ArgumentException>(() => store.EnqueueAsync(new CarMessageRequest(CarMessageRequestKind.Reply, " "), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => new CarConversationStore(""));
    }
}
