namespace CSharpFar.App.Editor;

public sealed class EditorUndoHistory
{
    private readonly int _limit;
    private readonly Stack<EditorTransaction> _undo = new();
    private readonly Stack<EditorTransaction> _redo = new();

    public EditorUndoHistory(int limit)
    {
        _limit = Math.Max(0, limit);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(EditorTransaction transaction)
    {
        if (_limit == 0)
            return;

        _undo.Push(transaction);
        _redo.Clear();
        while (_undo.Count > _limit)
            DropOldestUndo();
    }

    public EditorTransaction? PopUndo()
    {
        if (!_undo.TryPop(out var transaction))
            return null;

        _redo.Push(transaction);
        return transaction;
    }

    public EditorTransaction? PopRedo()
    {
        if (!_redo.TryPop(out var transaction))
            return null;

        _undo.Push(transaction);
        return transaction;
    }

    private void DropOldestUndo()
    {
        var kept = _undo.Reverse().Skip(1).ToArray();
        _undo.Clear();
        foreach (var transaction in kept.Reverse())
            _undo.Push(transaction);
    }
}
