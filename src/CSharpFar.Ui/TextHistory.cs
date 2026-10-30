namespace CSharpFar.Ui;

/// <summary>Persistent suggestion values shared by fields with one stable history ID.</summary>
public sealed class TextHistory
{
    public const int MaxItemsPerField = 100;

    private readonly object _sync = new();
    private readonly List<string> _items;
    private readonly Action<IReadOnlyList<string>>? _itemsChanged;
    private readonly Func<string, IReadOnlyList<string>?>? _promote;
    private readonly Func<IReadOnlyList<string>>? _reload;

    internal TextHistory(IEnumerable<string>? initialItems, Action<IReadOnlyList<string>>? itemsChanged)
    {
        _items = Normalize(initialItems);
        _itemsChanged = itemsChanged;
    }

    internal TextHistory(IEnumerable<string>? initialItems,
        Func<string, IReadOnlyList<string>?> promote, Func<IReadOnlyList<string>> reload)
        : this(initialItems, itemsChanged: null)
    {
        _promote = promote;
        _reload = reload;
    }

    public IReadOnlyList<string> Items
    {
        get { lock (_sync) return _items.ToArray(); }
    }

    public bool HasItems
    {
        get { lock (_sync) return _items.Count > 0; }
    }

    public void Refresh()
    {
        lock (_sync)
        {
            if (_reload is not null)
                Replace(_reload());
        }
    }

    public void Add(string text) => AddCore(text, allowWhitespaceOnly: false);

    public void AddPreservingWhitespace(string text) => AddCore(text, allowWhitespaceOnly: true);

    private void AddCore(string text, bool allowWhitespaceOnly)
    {
        if (string.IsNullOrEmpty(text) || !allowWhitespaceOnly && string.IsNullOrWhiteSpace(text))
            return;

        lock (_sync)
        {
            if (_promote is not null)
            {
                IReadOnlyList<string>? saved = _promote(text);
                if (saved is not null)
                    Replace(saved);
                return;
            }

            if (_items.Count > 0 && string.Equals(_items[0], text, StringComparison.Ordinal))
                return;

            _items.RemoveAll(item => string.Equals(item, text, StringComparison.Ordinal));
            _items.Insert(0, text);
            if (_items.Count > MaxItemsPerField)
                _items.RemoveRange(MaxItemsPerField, _items.Count - MaxItemsPerField);
            _itemsChanged?.Invoke(_items.ToArray());
        }
    }

    private void Replace(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(Normalize(items));
    }

    private static List<string> Normalize(IEnumerable<string>? items)
    {
        var result = new List<string>();
        if (items is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string item in items)
        {
            if (!string.IsNullOrEmpty(item) && seen.Add(item))
                result.Add(item);
            if (result.Count == MaxItemsPerField)
                break;
        }
        return result;
    }
}
