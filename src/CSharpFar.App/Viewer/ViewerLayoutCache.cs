namespace CSharpFar.App.Viewer;

// Bounded, access-ordered cache for prepared viewer layouts. State and scanner
// invalidation remain the responsibility of the viewer.
internal sealed class ViewerLayoutCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries = new();
    private readonly LinkedList<Entry> _recent = new();

    public ViewerLayoutCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (!_entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            value = default!;
            return false;
        }

        _recent.Remove(node);
        _recent.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public void Set(TKey key, TValue value)
    {
        if (_entries.Remove(key, out LinkedListNode<Entry>? previous))
            _recent.Remove(previous);

        var entry = new Entry(key, value);
        _entries[key] = _recent.AddFirst(entry);

        if (_entries.Count > _capacity)
        {
            LinkedListNode<Entry> oldest = _recent.Last!;
            _recent.RemoveLast();
            _entries.Remove(oldest.Value.Key);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _recent.Clear();
    }

    private sealed record Entry(TKey Key, TValue Value);
}
