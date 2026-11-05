using CarKit.Maui.Communication;
using Xunit;
using static CarKit.Maui.Tests.Communication.Conversations;

namespace CarKit.Maui.Tests.Communication;

// Message notification content, shared by MessagingStyle and UNNotificationContent
public class NotificationContentTests
{
    [Fact]
    public void One_to_one_title_is_the_sender()
    {
        var conversation = Chat();
        var message = Message("m9", Anna, "I'm here");

        var content = CarMessageNotificationContent.From(conversation, message);

        Assert.Equal("Anna", content.Title);
        Assert.Null(content.Subtitle);
        Assert.Equal("Anna", content.Sender);
        Assert.Equal(["m1", "m9"], content.Messages.Select(m => m.Id));
    }

    [Fact]
    public void Group_conversation_title_and_sender()
    {
        var content = CarMessageNotificationContent.From(Chat("team", group: true), Message("m9", Peter, "I'll be late"));

        Assert.Equal("Team", content.Title);
        Assert.Equal("Peter", content.Subtitle);
        Assert.Equal("Peter", content.Sender);
    }

    [Fact]
    public void Message_from_self_names_self()
    {
        var content = CarMessageNotificationContent.From(Chat(), Message("m9", null, "Coming now"));

        Assert.Equal("Me", content.Sender);
    }

    [Fact]
    public void New_message_is_appended_once()
    {
        var conversation = Chat(count: 3);

        var content = CarMessageNotificationContent.From(conversation, conversation.Messages[1]);

        Assert.Equal(["m1", "m2", "m3"], content.Messages.Select(m => m.Id));
        Assert.Same(conversation.Messages[1], content.Message);
    }

    [Fact]
    public void At_most_25_messages()
    {
        var content = CarMessageNotificationContent.From(Chat(count: 30), Message("new", Anna, "Last"));

        Assert.Equal(25, content.Messages.Count);
        Assert.Equal("m7", content.Messages[0].Id);
        Assert.Equal("new", content.Messages[^1].Id);
    }

    [Fact]
    public void Reply_is_added_as_a_read_message_from_self()
    {
        var content = CarMessageNotificationContent.From(Chat("team", group: true), Message("m9", Peter, "Where are you?"));

        var replied = content.WithReply("In 5 minutes", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_123_000));

        var reply = replied.Messages[^1];
        Assert.Null(reply.Sender);
        Assert.True(reply.IsRead);
        Assert.Equal("In 5 minutes", reply.Text);
        Assert.Equal("carkit-reply-1700000123000", reply.Id);
        Assert.Equal(["m1", "m9", reply.Id], replied.Messages.Select(m => m.Id));
        Assert.Equal("Team", replied.Title);
        Assert.Equal("Peter", replied.Subtitle);
    }

    [Fact]
    public void Request_codes_differ_per_conversation_and_action()
    {
        var codes = new[]
        {
            CarMessageNotificationContent.RequestCode("anna", CarMessageNotificationContent.ReplyAction),
            CarMessageNotificationContent.RequestCode("anna", CarMessageNotificationContent.MarkAsReadAction),
            CarMessageNotificationContent.RequestCode("team", CarMessageNotificationContent.ReplyAction),
            CarMessageNotificationContent.RequestCode("team", CarMessageNotificationContent.MarkAsReadAction),
        };

        Assert.Equal(4, codes.Distinct().Count());
        Assert.All(codes, c => Assert.True(c >= 0));
        // Stable across processes (FNV-1a), unlike string.GetHashCode
        Assert.Equal(codes[0], CarMessageNotificationContent.RequestCode("anna", "reply"));
        Assert.Equal(692_878_806, CarMessageNotificationContent.RequestCode("a", "b")); // FNV-1a("a|b") & 0x7FFFFFFF (Python)
    }
}
