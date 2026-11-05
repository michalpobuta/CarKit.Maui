using Microsoft.Extensions.Logging;

namespace CarKit.Maui.Renderers;

// Content limits read from the platform (CPListTemplate.MaximumItemCount, ConstraintManager.GetContentLimit) once per
// connection. A read failure or a value ≤ 0 → null, meaning the default from the rules (CarCapabilities.GetLimit).
internal sealed class CarRuntimeLimits(Func<CarLimitKind, int?> read, ILogger logger)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<CarLimitKind, int?> _cache = [];

    public int? Get(CarLimitKind kind)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(kind, out var cached))
            {
                return cached;
            }
        }

        int? value;
        try
        {
            value = read(kind) is > 0 and var limit ? limit : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CarKitRenderLog.LimitUnavailable(logger, kind, ex);
            value = null;
        }

        lock (_lock)
        {
            _cache[kind] = value;
        }

        return value;
    }
}
