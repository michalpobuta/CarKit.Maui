using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

/// <summary>Runs work on the platform main thread; every native call and user callback goes through it.</summary>
public interface ICarDispatcher
{
    /// <summary>Gets a value indicating whether the caller is off the main thread.</summary>
    bool IsDispatchRequired { get; }

    /// <summary>Runs <paramref name="work"/> on the main thread.</summary>
    /// <param name="work">The work.</param>
    /// <returns>A task that completes when the work completes.</returns>
    Task DispatchAsync(Func<Task> work);

    /// <summary>Runs <paramref name="work"/> on the main thread.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>The result of the work.</returns>
    Task<T> DispatchAsync<T>(Func<Task<T>> work);
}

/// <summary>Receives exceptions thrown by user callbacks, so they never crash the car session.</summary>
public interface ICarErrorHandler
{
    /// <summary>Handles an exception thrown by a user callback.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="context">Where it happened.</param>
    void HandleError(Exception exception, CarErrorContext context);
}

/// <summary>Where a callback exception happened.</summary>
public sealed class CarErrorContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="operation">The operation, such as <c>Select</c> or <c>OnConnected</c>.</param>
    /// <param name="element">The element whose callback failed.</param>
    public CarErrorContext(string operation, CarElement? element)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation;
        Element = element;
    }

    /// <summary>Gets the operation.</summary>
    public string Operation { get; }

    /// <summary>Gets the element whose callback failed.</summary>
    public CarElement? Element { get; }
}

// Default error handling: log at the Error level
internal sealed class LoggingCarErrorHandler(ILogger logger) : ICarErrorHandler
{
    public void HandleError(Exception exception, CarErrorContext context) =>
        CarKitLog.HandlerFailed(logger, context.Operation, exception);
}
