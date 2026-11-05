using CarKit.Maui.Core.Tests.Session;
using CarKit.Maui.Core.Tests.Validation;
using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Model;

// Stage 6: conversation model (spec: "Communication module decisions (Stage 6)")
internal static class C
{
    public static readonly CarPerson Me = new("me", "Me");
    public static readonly CarPerson Anna = new("anna", "Anna") { PhoneOrEmail = "+15550100200" };

    public static CarConversationItem Conversation(string id = "anna-chat", params CarConversationMessage[] messages) =>
        new(id, "Anna", Me, messages.Length > 0 ? messages : [new CarConversationMessage("m1", Anna, "Hi", DateTimeOffset.UnixEpoch)]);

    public static CarListTemplate List(params CarListTemplateItem[] items) => new("Messages") { Id = "messages", Sections = [new CarSection(items)] };
}

public class ConversationModelTests
{
    [Fact]
    public void Person_needs_key_and_name()
    {
        Assert.Throws<ArgumentException>(() => new CarPerson(" ", "Anna"));
        Assert.Throws<ArgumentNullException>(() => new CarPerson("anna", null!));
        Assert.Equal("+15550100200", C.Anna.PhoneOrEmail);
    }

    [Fact]
    public void Message_sender_null_means_self()
    {
        var mine = new CarConversationMessage("m2", null, "Hey", DateTimeOffset.UnixEpoch);

        Assert.Null(mine.Sender);
        Assert.False(mine.IsRead);
        Assert.Throws<ArgumentException>(() => new CarConversationMessage("", C.Anna, "x", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentNullException>(() => new CarConversationMessage("m3", C.Anna, null!, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Conversation_id_is_the_element_id()
    {
        var conversation = C.Conversation("chat-42");

        Assert.Equal("chat-42", conversation.Id);
        Assert.Equal("Anna", conversation.Title.Text);
        Assert.Same(C.Me, conversation.Self);
        Assert.Throws<ArgumentNullException>(() => new CarConversationItem("x", "t", C.Me, null!));
        Assert.Throws<ArgumentNullException>(() => conversation with { Self = null! });
        Assert.Throws<ArgumentNullException>(() => conversation with { Title = null! });
    }

    [Fact]
    public void Unread_follows_messages()
    {
        var read = new CarConversationMessage("m1", C.Anna, "Hi", DateTimeOffset.UnixEpoch) { IsRead = true };
        var unread = new CarConversationMessage("m2", C.Anna, "Are you there?", DateTimeOffset.UnixEpoch);

        Assert.False(C.Conversation("c", read).IsUnread);
        Assert.True(C.Conversation("c", read, unread).IsUnread);
    }

    [Fact]
    public void Detail_defaults_to_the_last_message()
    {
        var conversation = C.Conversation("c",
            new CarConversationMessage("m1", C.Anna, "First", DateTimeOffset.UnixEpoch),
            new CarConversationMessage("m2", C.Anna, "Last", DateTimeOffset.UnixEpoch));

        Assert.Equal("Last", conversation.DetailText);
        Assert.Equal("Explicit", (conversation with { Detail = "Explicit" }).DetailText);
        Assert.Null((conversation with { Messages = [] }).DetailText);
    }

    [Fact]
    public void Walker_finds_conversation_rows() =>
        Assert.IsType<CarConversationItem>(CarModelWalker.Find(C.List(C.Conversation("c1")), "c1"));
}

public class ConversationValidationTests
{
    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public void Conversation_rows_are_allowed_in_communication(CarPlatform platform)
    {
        var list = C.List(C.Conversation("c1"), new CarListItem("Contacts"));

        Assert.Same(list, V.Create(CarAppCategory.Communication, platform).Prepare(list, resultingDepth: 1));
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    public void Conversation_rows_outside_communication_are_rejected(CarPlatform platform)
    {
        var ex = Assert.Throws<CarTemplateNotAllowedException>(() =>
            V.Create(CarAppCategory.Parking, platform).Prepare(C.List(C.Conversation("c1")), resultingDepth: 1));

        Assert.Equal(CarTemplateKind.List, ex.Kind);
        Assert.Equal(CarAppCategory.Parking, ex.Categories);
        Assert.Contains("Conversation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mixed_categories_allow_conversations_with_communication() =>
        Assert.NotNull(V.Create(CarAppCategory.Parking | CarAppCategory.Communication).Prepare(C.List(C.Conversation("c1")), 1));

    [Fact]
    public void Conversation_without_messages_is_rejected()
    {
        var empty = C.Conversation("c1") with { Messages = [] };

        var ex = Assert.Throws<CarKitException>(() => V.Create(CarAppCategory.Communication).Prepare(C.List(empty), 1));

        Assert.Contains("at least one message", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_with_on_selected_is_rejected()
    {
        var selectable = C.Conversation("c1") with { OnSelected = _ => Task.CompletedTask };

        var ex = Assert.Throws<CarKitException>(() => V.Create(CarAppCategory.Communication, CarPlatform.AndroidAuto).Prepare(C.List(selectable), 1));

        Assert.Contains("OnReply", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Warn_mode_logs_instead_of_throwing()
    {
        var logger = new TestLogger();
        var validator = V.Create(CarAppCategory.Parking, configure: o => o.Validation = CarValidationMode.Warn, logger: logger);

        Assert.NotNull(validator.Prepare(C.List(C.Conversation("c1") with { Messages = [] }), 1));
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void Conversation_rows_in_tabs_are_checked()
    {
        var tabs = new CarTabTemplate([C.List(C.Conversation("c1")) with { TabInfo = new CarTabInfo("Chats") }, new CarGridTemplate("Grid") { TabInfo = new CarTabInfo("Grid") }]);

        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Parking).Prepare(tabs, 1));
        Assert.NotNull(V.Create(CarAppCategory.Communication).Prepare(tabs, 1));
    }

    [Fact]
    public void Conversation_rows_count_toward_the_list_limit()
    {
        var list = C.List([.. Enumerable.Range(0, 10).Select(i => (CarListTemplateItem)C.Conversation("c" + i))]);

        var prepared = (CarListTemplate)V.Create(CarAppCategory.Communication, CarPlatform.AndroidAuto).Prepare(list, 1);

        Assert.Equal(6, prepared.Sections[0].Items.Count);
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay, null, "14.0")]
    [InlineData(CarPlatform.AndroidAuto, 7, null)]
    public void Rules_declare_the_conversation_requirement(CarPlatform platform, int? apiLevel, string? iosVersion)
    {
        var rules = CarPlatformRules.For(platform);

        var requirement = rules.Items[CarListItemKinds.Conversation];
        Assert.Equal(apiLevel, requirement.MinApiLevel);
        Assert.Equal(iosVersion is null ? null : Version.Parse(iosVersion), requirement.MinIosVersion);
        Assert.Contains(CarListItemKinds.Conversation, rules.Categories[CarAppCategory.Communication].Items);
        Assert.Empty(rules.Categories[CarAppCategory.Parking].Items);
    }

    [Theory]
    [InlineData("""{ "items": { "Bogus": {} } }""")]
    [InlineData("""{ "categories": { "Communication": { "templates": {}, "maxDepth": 5, "items": [ "Bogus" ] } } }""")]
    public void Rules_reject_unknown_item_kinds(string json) =>
        Assert.Throws<InvalidDataException>(() => CarPlatformRules.Parse(json));

    [Fact]
    public void Only_conversations_have_an_item_kind()
    {
        Assert.Equal(CarListItemKinds.Conversation, CarListItemKinds.Of(C.Conversation()));
        Assert.Null(CarListItemKinds.Of(new CarListItem("x")));
    }
}

public class FakeCarHostConversationTests
{
    [Fact]
    public async Task Reply_and_mark_as_read_run_handlers_on_the_main_thread()
    {
        var calls = new List<string>();
        using var host = new FakeCarHost(CarAppCategory.Communication, CarPlatform.AndroidAuto);
        var conversation = C.Conversation("c1") with
        {
            OnReply = context =>
            {
                calls.Add($"reply:{context.Element.Id}:{context.Text}:{!host.Dispatcher.IsDispatchRequired}");
                return Task.CompletedTask;
            },
            OnMarkAsRead = context =>
            {
                calls.Add($"read:{context.Element.Id}:{!host.Dispatcher.IsDispatchRequired}");
                return Task.CompletedTask;
            },
        };
        await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(C.List(conversation))));

        await host.ReplyAsync(conversation, "On my way");
        await host.MarkAsReadAsync(conversation);

        Assert.Equal(["reply:c1:On my way:True", "read:c1:True"], calls);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Missing_handlers_are_a_no_op()
    {
        using var host = new FakeCarHost(CarAppCategory.Communication, CarPlatform.AndroidAuto);
        var conversation = C.Conversation("c1");
        await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(C.List(conversation, new CarListItem("Contact") { Id = "contact" }))));

        await host.ReplyAsync(conversation, "x");
        await host.MarkAsReadAsync(conversation);

        Assert.Empty(host.Errors);
        // Id of an element that is not a conversation
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ReplyAsync(C.Conversation("contact"), "x"));
    }

    // Review M13: CarPlay hands the conversation to Siri and never calls OnReply/OnMarkAsRead
    [Fact]
    public async Task CarPlay_host_does_not_call_conversation_handlers()
    {
        using var host = new FakeCarHost(CarAppCategory.Communication);
        var conversation = C.Conversation("c1") with { OnReply = _ => throw new InvalidOperationException("never") };
        await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(C.List(conversation))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ReplyAsync(conversation, "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.MarkAsReadAsync(conversation));
    }
}
