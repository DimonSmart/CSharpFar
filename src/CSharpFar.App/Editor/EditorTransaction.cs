namespace CSharpFar.App.Editor;

public sealed class EditorTransaction
{
    public EditorTransaction(
        string name,
        EditorTextChange change,
        EditorPosition beforeCursor,
        EditorPosition afterCursor,
        EditorSelection? beforeSelection,
        EditorSelection? afterSelection)
    {
        Name = name;
        Change = change;
        BeforeCursor = beforeCursor;
        AfterCursor = afterCursor;
        BeforeSelection = beforeSelection;
        AfterSelection = afterSelection;
    }

    public string Name { get; }
    public EditorTextChange Change { get; }
    public EditorPosition BeforeCursor { get; }
    public EditorPosition AfterCursor { get; }
    public EditorSelection? BeforeSelection { get; }
    public EditorSelection? AfterSelection { get; }
    internal long BeforeRevision { get; set; }
    internal long AfterRevision { get; set; }
}
