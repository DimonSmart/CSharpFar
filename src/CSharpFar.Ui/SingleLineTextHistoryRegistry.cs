namespace CSharpFar.Ui;

public sealed class SingleLineTextHistoryRegistry : ITextFieldHistoryProvider
{
    private readonly ISingleLineTextHistoryStore _store;
    private readonly Dictionary<TextHistoryId, TextHistory> _histories = [];
    private readonly object _sync = new();

    public SingleLineTextHistoryRegistry(ISingleLineTextHistoryStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public TextHistory Get(TextHistoryId id)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("A valid text history ID is required.", nameof(id));

        lock (_sync)
        {
            if (_histories.TryGetValue(id, out TextHistory? history))
                return history;

            history = new TextHistory(_store.Load(id.Value), items => _store.Save(id.Value, items));
            _histories.Add(id, history);
            return history;
        }
    }
}
