using Microsoft.Extensions.Logging;

namespace CarKit.Sample.Navigation;

// CarKit and sample logs go to stdout (simctl launch --console-pty); the MAUI DebugLogger writes only when a debugger
// is attached
internal sealed class ConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class ConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && category.StartsWith("CarKit", StringComparison.Ordinal);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Console.WriteLine($"{category}: {formatter(state, exception)}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
            }
        }
    }
}
