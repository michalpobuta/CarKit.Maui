namespace CarKit.Maui.Renderers;

// LRU cache of decoded images for the car; no synchronization of its own (CarImageLoader holds the lock)
internal sealed class CarImageCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = [];
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = [];

    public int Count => _map.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.Remove(key, out var existing))
        {
            _order.Remove(existing);
        }

        _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        while (_map.Count > capacity && _order.Last is { } last)
        {
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }
}
