namespace CSharpFar.Ui;

public interface ISingleLineTextHistoryStore
{
    IReadOnlyList<string> Load(string fieldKey);

    /// <summary>Promotes a value against the current store state; null means persistence failed.</summary>
    IReadOnlyList<string>? Promote(string fieldKey, string value);
}

public sealed class InMemorySingleLineTextHistoryStore : ISingleLineTextHistoryStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<string>> _fields = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Load(string fieldKey)
    {
        lock (_sync)
            return _fields.TryGetValue(fieldKey, out var items) ? items.ToArray() : [];
    }

    public IReadOnlyList<string>? Promote(string fieldKey, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            if (!_fields.TryGetValue(fieldKey, out var items))
                _fields[fieldKey] = items = [];

            if (value.Length > 0)
            {
                items.RemoveAll(item => string.Equals(item, value, StringComparison.Ordinal));
                items.Insert(0, value);
                if (items.Count > TextHistory.MaxItemsPerField)
                    items.RemoveRange(TextHistory.MaxItemsPerField, items.Count - TextHistory.MaxItemsPerField);
            }
            return items.ToArray();
        }
    }
}
