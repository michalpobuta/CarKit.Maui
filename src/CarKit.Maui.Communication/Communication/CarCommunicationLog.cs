using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Communication;

// Messages of the Communication module (category CarKit.Communication), allocation-free and AOT-compatible
internal static partial class CarCommunicationLog
{
    [LoggerMessage(400, LogLevel.Warning, "CarKit cannot show the message notification of conversation '{ConversationId}': {Reason}")]
    public static partial void NotificationSkipped(ILogger logger, string conversationId, string reason);

    [LoggerMessage(401, LogLevel.Information, "CarKit communication: {Message}")]
    public static partial void Info(ILogger logger, string message);

    [LoggerMessage(402, LogLevel.Warning, "CarKit communication: {Message}")]
    public static partial void Warning(ILogger logger, string message, Exception? exception);
}
