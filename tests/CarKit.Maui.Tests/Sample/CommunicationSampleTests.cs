using CarKit.Maui.Communication;
using CarKit.Maui.Testing;
using CarKit.Sample.Communication;
using CarKit.Sample.Communication.Intents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarKit.Maui.Tests.Sample;

// Communication sample (Stage 6 acceptance, without hosts): the car screens pass Strict validation in the Communication
// category on CarPlay and Android Auto, a voice reply and a read mark from AA change the conversation, and the change
// refreshes the list in place
public class CommunicationSampleTests
{
    public static TheoryData<CarPlatform> Platforms => [CarPlatform.CarPlay, CarPlatform.AndroidAuto];

    private sealed class RecordingCalls : ICarCallManager
    {
        public List<string> Started { get; } = [];

        public bool IsSupported { get; init; } = true;

        public IReadOnlyList<CarCall> Calls => [];

        public event EventHandler<CarCall>? CallChanged
        {
            add { }
            remove { }
        }

        public Task<CarCall> ReportIncomingCallAsync(CarCallHandle caller, string? displayName = null) => throw new NotSupportedException();

        public Task<CarCall> StartOutgoingCallAsync(CarCallHandle callee, string? displayName = null)
        {
            Started.Add($"{callee.Type}:{callee.Value}:{displayName}");
            return Task.FromResult(new CarCall(Guid.NewGuid(), callee, CarCallDirection.Outgoing) { State = CarCallState.Dialing });
        }

        public Task EndCallAsync(Guid callId) => Task.CompletedTask;

        public Task SetHeldAsync(Guid callId, bool onHold) => Task.CompletedTask;

        public Task SetMutedAsync(Guid callId, bool muted) => Task.CompletedTask;

        public void ReportOutgoingCallConnected(Guid callId)
        {
        }

        public void ReportCallEnded(Guid callId, CarCallEndReason reason)
        {
        }
    }

    private static CarListTemplate Root(FakeCarHost host) => Assert.IsType<CarListTemplate>(host.Session!.Stack[0]);

    [Theory, MemberData(nameof(Platforms))]
    public async Task Root_lists_conversations_and_contacts(CarPlatform platform)
    {
        using var host = new FakeCarHost(CarAppCategory.Communication, platform);
        await host.ConnectAsync(new SampleCommunicationCarHandler(new SampleConversations(), new RecordingCalls()));

        var root = Root(host);
        Assert.Equal(["anna", "team"], root.Sections[0].Items.Select(i => i.Id));
        Assert.All(root.Sections[0].Items, item => Assert.IsType<CarConversationItem>(item));
        Assert.Equal(3, root.Sections[1].Items.Count);
        Assert.Empty(host.Errors);
    }

    [Theory, MemberData(nameof(Platforms))]
    public async Task Contact_shows_call_and_message_or_the_fallback(CarPlatform platform)
    {
        using var host = new FakeCarHost(CarAppCategory.Communication, platform);
        var calls = new RecordingCalls();
        await host.ConnectAsync(new SampleCommunicationCarHandler(new SampleConversations(), calls));

        await host.SelectAsync(Root(host).Sections[1].Items[0]);

        // The template sent to the host (after the fallback), not the model from the session stack
        var top = host.Calls.Last(c => c.Operation == FakeCarHostOperation.Push).Template;
        if (platform == CarPlatform.CarPlay)
        {
            var contact = Assert.IsType<CarPlayContactTemplate>(top);
            Assert.Equal([CarPlayContactButtonKind.Call, CarPlayContactButtonKind.Message], contact.Buttons.Select(b => b.Kind));
            Assert.Equal("+15550100200", contact.Buttons[1].PhoneOrEmail);
            await host.SelectAsync(contact.Buttons[0]);
            Assert.Equal(["PhoneNumber:+15550100200:Anna Brown"], calls.Started);
        }
        else
        {
            Assert.IsType<CarInformationTemplate>(top);
        }

        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Android_auto_reply_and_mark_as_read_update_the_list_in_place()
    {
        using var host = new FakeCarHost(CarAppCategory.Communication, CarPlatform.AndroidAuto);
        var conversations = new SampleConversations();
        await host.ConnectAsync(new SampleCommunicationCarHandler(conversations, new RecordingCalls()));
        var anna = (CarConversationItem)Root(host).Sections[0].Items[0];
        Assert.True(anna.IsUnread);

        await host.ReplyAsync(anna, "I'll be there in 5 minutes");
        await Task.Delay(50, TestContext.Current.CancellationToken);

        var updated = (CarConversationItem)Root(host).Sections[0].Items[0];
        Assert.False(updated.IsUnread);
        Assert.Equal("I'll be there in 5 minutes", updated.Messages[^1].Text);
        Assert.Null(updated.Messages[^1].Sender);
        Assert.Contains(host.Calls, c => c.Operation == FakeCarHostOperation.Update);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Incoming_message_updates_the_car_list_and_message_handler_replies()
    {
        using var host = new FakeCarHost(CarAppCategory.Communication, CarPlatform.CarPlay);
        var conversations = new SampleConversations();
        await host.ConnectAsync(new SampleCommunicationCarHandler(conversations, new RecordingCalls()));

        var (team, message) = conversations.Receive("team", SampleConversations.Peter, "I'll be late");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.True(((CarConversationItem)Root(host).Sections[0].Items[1]).IsUnread);
        Assert.Contains(message, team.Messages);

        var handler = new SampleMessageHandler(conversations, NullLogger<SampleMessageHandler>.Instance);
        await handler.OnReplyAsync("team", "OK", TestContext.Current.CancellationToken);
        await handler.OnMarkAsReadAsync("anna", TestContext.Current.CancellationToken);
        Assert.False(conversations.Find("team")!.IsUnread);
        Assert.False(conversations.Find("anna")!.IsUnread);
        Assert.Null(conversations.Reply("nope", "x"));
        Assert.Throws<ArgumentException>(() => conversations.Receive("nope", SampleConversations.Anna, "x"));
        Assert.Empty(host.Errors);
    }

    // Intents Extension logic (without the Intents types): a reply to a Siri conversation, message search, read mark
    [Fact]
    public void Intent_logic_finds_conversations_and_messages()
    {
        var all = new SampleConversations().All;

        Assert.Equal("team", SampleIntentLogic.FindConversation(all, "team", [])!.Id);
        Assert.Equal("anna", SampleIntentLogic.FindConversation(all, null, ["Anna Brown"])!.Id);
        Assert.Equal("anna", SampleIntentLogic.FindConversation(all, null, ["+15550100200"])!.Id);
        Assert.Null(SampleIntentLogic.FindConversation(all, null, ["Peter Wilson"]));
        Assert.Null(SampleIntentLogic.FindConversation(all, null, []));
        Assert.Equal(["anna"], SampleIntentLogic.Contacts(all, "anna brown").Select(p => p.Key));

        var unread = SampleIntentLogic.Search(all, [], [], [], unreadOnly: true);
        Assert.Equal(["m1"], unread.Select(x => x.Message.Id));
        Assert.Equal(["m3", "m4"], SampleIntentLogic.Search(all, ["team"], [], [], unreadOnly: false).Select(x => x.Message.Id));
        Assert.Equal(["m3"], SampleIntentLogic.Search(all, [], [], ["peter@example.com"], unreadOnly: false).Select(x => x.Message.Id));
        Assert.Equal(["m4"], SampleIntentLogic.Search(all, [], ["m4"], [], unreadOnly: false).Select(x => x.Message.Id));
        Assert.Equal(["anna", "team"], SampleIntentLogic.ConversationsOf(all, ["m1", "m3"]));
    }
}
