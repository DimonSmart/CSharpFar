namespace CSharpFar.Ui;

public sealed class SingleLineTextHistoryRegistry
{
    private readonly ISingleLineTextHistoryStore _store;
    private readonly Dictionary<string, SingleLineTextHistoryState> _histories = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public SingleLineTextHistoryRegistry() : this(new InMemorySingleLineTextHistoryStore()) { }

    public SingleLineTextHistoryRegistry(ISingleLineTextHistoryStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public SingleLineTextHistoryState GetOrCreate(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
            throw new ArgumentException("A field history key is required.", nameof(fieldKey));

        lock (_sync)
        {
            if (_histories.TryGetValue(fieldKey, out SingleLineTextHistoryState? history))
                return history;

            history = new SingleLineTextHistoryState(_store.Load(fieldKey), items => _store.Save(fieldKey, items));
            _histories.Add(fieldKey, history);
            return history;
        }
    }
}
