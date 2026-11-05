using System.Runtime.CompilerServices;

namespace CarKit.Maui.DeviceTests;

internal sealed class CheckException(string message) : Exception(message);

// Assertions of the device test runner
internal static class Check
{
    public static void True(bool condition, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        if (!condition)
        {
            throw new CheckException($"Expected true: {expression}");
        }
    }

    public static void Equal<T>(T expected, T actual, [CallerArgumentExpression(nameof(actual))] string? expression = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new CheckException($"{expression}: expected <{expected}>, got <{actual}>");
        }
    }

    public static void Same(object? expected, object? actual, [CallerArgumentExpression(nameof(actual))] string? expression = null)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new CheckException($"{expression}: expected the same instance");
        }
    }

    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? expression = null) where T : class =>
        value ?? throw new CheckException($"Expected non-null: {expression}");

    public static void Null<T>(T? value, [CallerArgumentExpression(nameof(value))] string? expression = null) where T : class
    {
        if (value is not null)
        {
            throw new CheckException($"Expected null: {expression}, got <{value}>");
        }
    }

    public static T Is<T>(object? value, [CallerArgumentExpression(nameof(value))] string? expression = null) where T : class =>
        value as T ?? throw new CheckException($"{expression}: expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}");

    // Waits for a condition, yielding the main thread (images from a URI/stream, handlers through the dispatcher)
    public static async Task Eventually(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
            {
                throw new CheckException($"Timed out waiting for {what}");
            }

            await Task.Delay(20);
        }
    }
}
