using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Communication;

// Sample log (category CarKit.Sample…, visible in logcat and the simulator console)
internal static partial class SampleLog
{
    [LoggerMessage(900, LogLevel.Information, "CarKit sample: {Message}")]
    public static partial void Info(ILogger logger, string message);
}
