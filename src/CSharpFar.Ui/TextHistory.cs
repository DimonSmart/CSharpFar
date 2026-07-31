namespace CSharpFar.Ui;

/// <summary>Persistent suggestion values shared by fields with one stable history ID.</summary>
public sealed class TextHistory
{
    public const int MaxItemsPerField = 100;

    private readonly List<string> _items;
    private readonly Action<IReadOnlyList<string>>? _itemsChanged;

    internal TextHistory(IEnumerable<string>? initialItems, Action<IReadOnlyList<string>>? itemsChanged)
    {
        _items = Normalize(initialItems);
        _itemsChanged = itemsChanged;
    }

    public IReadOnlyList<string> Items => _items;
    public bool HasItems => _items.Count > 0;

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (_items.Count > 0 && string.Equals(_items[0], text, StringComparison.Ordinal))
            return;

        _items.RemoveAll(item => string.Equals(item, text, StringComparison.Ordinal));
        _items.Insert(0, text);
        if (_items.Count > MaxItemsPerField)
            _items.RemoveRange(MaxItemsPerField, _items.Count - MaxItemsPerField);
        _itemsChanged?.Invoke(_items);
    }

    private static List<string> Normalize(IEnumerable<string>? items)
    {
        var result = new List<string>();
        if (items is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string item in items)
        {
            if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
                result.Add(item);
            if (result.Count == MaxItemsPerField)
                break;
        }
        return result;
    }
}
