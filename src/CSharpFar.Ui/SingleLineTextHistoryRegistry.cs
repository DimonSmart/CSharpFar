namespace CSharpFar.Ui;

public sealed class SingleLineTextHistoryRegistry : ITextFieldHistoryProvider
{
    private readonly ISingleLineTextHistoryStore _store;
    private readonly Dictionary<TextHistoryId, TextHistory> _histories = [];
    private readonly object _sync = new();

    [Obsolete("Use SingleLineTextHistoryRegistry(ISingleLineTextHistoryStore) or TextFieldHistoryTestFactory.CreateInMemory().")]
    public SingleLineTextHistoryRegistry() : this(new InMemorySingleLineTextHistoryStore()) { }

    public SingleLineTextHistoryRegistry(ISingleLineTextHistoryStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public TextHistory Get(TextHistoryId id)
    {
        lock (_sync)
        {
            if (_histories.TryGetValue(id, out TextHistory? history))
                return history;

            history = new TextHistory(_store.Load(id.Value), items => _store.Save(id.Value, items));
            _histories.Add(id, history);
            return history;
        }
    }

    public TextHistory GetOrCreate(TextHistoryId id) => Get(id);

    [Obsolete("Use Get(TextHistoryId) with a centralized stable identifier.")]
    public TextHistory GetOrCreate(string fieldKey) => Get(new TextHistoryId(fieldKey));
}
