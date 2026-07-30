namespace CSharpFar.Ui;

public interface ISingleLineTextHistoryStore
{
    IReadOnlyList<string> Load(string fieldKey);

    void Save(string fieldKey, IReadOnlyList<string> items);
}

public sealed class InMemorySingleLineTextHistoryStore : ISingleLineTextHistoryStore
{
    private readonly Dictionary<string, List<string>> _fields = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Load(string fieldKey) =>
        _fields.TryGetValue(fieldKey, out List<string>? items) ? items.ToArray() : [];

    public void Save(string fieldKey, IReadOnlyList<string> items) =>
        _fields[fieldKey] = items.ToList();
}
