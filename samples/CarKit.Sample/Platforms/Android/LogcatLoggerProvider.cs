using Microsoft.Extensions.Logging;
using AndroidLog = Android.Util.Log;

namespace CarKit.Sample;

// CarKit logs (ILogger) go to logcat with the CarKit.Sample tag; the MAUI DebugLogger writes only when a debugger is
// attached
internal sealed class LogcatLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogcatLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class LogcatLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && category.StartsWith("CarKit", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{category}: {formatter(state, exception)}{(exception is null ? string.Empty : Environment.NewLine + exception)}";
            if (logLevel >= LogLevel.Error)
            {
                AndroidLog.Error("CarKit.Sample", message);
            }
            else
            {
                AndroidLog.Info("CarKit.Sample", message);
            }
        }
    }
}
