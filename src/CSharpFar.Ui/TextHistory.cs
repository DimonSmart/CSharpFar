namespace CSharpFar.Ui;

/// <summary>Persistent values and transient suggestion state for one text field.</summary>
public sealed class TextHistory : SingleLineTextHistoryState
{
    internal TextHistory(IEnumerable<string>? initialItems, Action<IReadOnlyList<string>>? itemsChanged)
        : base(initialItems, itemsChanged) { }
}
