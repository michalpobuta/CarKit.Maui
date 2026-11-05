namespace CarKit.Maui.Media;

internal static class CarMediaPaging
{
    // A page of the list (Media3 OnGetSearchResult, results from memory); out of range → empty
    public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        if (page < 0 || pageSize <= 0)
        {
            return [];
        }

        var start = (long)page * pageSize;
        return start >= items.Count ? [] : [.. items.Skip((int)start).Take(pageSize)];
    }
}

// OnSearch results for OnGetSearchResult (Media3: search first, then the result pages) per controller and query.
// A small memory (the last 8 queries), because the host usually asks for the result right after it is reported.
// Thread-safe.
internal sealed class CarMediaSearchCache
{
    private const int Capacity = 8;
    private readonly LinkedList<(string Key, IReadOnlyList<CarMediaItem> Results)> _entries = [];
    private readonly Lock _lock = new();

    public void Store(string controller, string query, IReadOnlyList<CarMediaItem> results)
    {
        var key = Key(controller, query);
        lock (_lock)
        {
            Remove(key);
            _entries.AddFirst((key, results));
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }
    }

    public bool TryGet(string controller, string query, out IReadOnlyList<CarMediaItem> results)
    {
        var key = Key(controller, query);
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Key == key)
                {
                    results = entry.Results;
                    return true;
                }
            }
        }

        results = [];
        return false;
    }

    private static string Key(string controller, string query) => controller + "\n" + query;

    private void Remove(string key)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (node.Value.Key == key)
            {
                _entries.Remove(node);
                return;
            }
        }
    }
}
