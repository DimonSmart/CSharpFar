using System.Collections.Concurrent;

namespace CSharpFar.Ui;

public sealed class SingleLineTextHistoryRegistry
{
    private readonly ConcurrentDictionary<string, SingleLineTextHistoryState> _histories = new();

    public SingleLineTextHistoryState GetOrCreate(string fieldKey) =>
        _histories.GetOrAdd(fieldKey, _ => new SingleLineTextHistoryState());
}
