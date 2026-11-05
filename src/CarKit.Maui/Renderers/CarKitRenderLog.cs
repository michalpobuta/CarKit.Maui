using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Renderers;

// Renderer messages (category CarKit.Hosting), allocation-free and AOT-compatible
internal static partial class CarKitRenderLog
{
    [LoggerMessage(200, LogLevel.Warning, "CarKit could not read the {Limit} limit from the platform; using the rule default")]
    public static partial void LimitUnavailable(ILogger logger, CarLimitKind limit, Exception exception);

    [LoggerMessage(201, LogLevel.Warning, "CarKit shows no image for {Source}: {Reason}")]
    public static partial void ImageFailed(ILogger logger, string source, string reason, Exception? exception);

    [LoggerMessage(202, LogLevel.Warning, "CarPlay cannot change the title or layout of {Kind} '{TemplateId}' below the top of the stack; the rest was updated in place")]
    public static partial void FixedPropertyKept(ILogger logger, CarTemplateKind kind, string templateId);

    [LoggerMessage(203, LogLevel.Warning, "CarKit did not run the Android Auto hooks of {Kind} '{TemplateId}': none of them configures {Builder}, which this host uses. Lists use ConfigureAndroidAuto (ListTemplate.Builder) below Car App API 8 and ConfigureAndroidAutoSectioned (SectionedItemTemplate.Builder) from API 8")]
    public static partial void AndroidAutoHooksNotApplied(ILogger logger, CarTemplateKind kind, string templateId, string builder);

    [LoggerMessage(204, LogLevel.Warning, "Android Auto sent {Callback} for conversation '{ConversationId}', which has no handler; set OnReply and OnMarkAsRead on CarConversationItem")]
    public static partial void ConversationHandlerMissing(ILogger logger, string callback, string conversationId);

    // Stage 7 (final review I1): OnRefreshRequested without a button in the car (a list without a map or a host below
    // Car App API 5)
    [LoggerMessage(205, LogLevel.Warning, "Android Auto shows no refresh button for {Kind} '{TemplateId}': {Reason}; OnRefreshRequested is not called")]
    public static partial void RefreshUnavailable(ILogger logger, CarTemplateKind kind, string templateId, string reason);
}
