namespace CSharpFar.App.Editor;

public sealed class EditorRedrawEventArgs : EditorSessionEventArgs
{
    public EditorRedrawEventArgs(EditorSession session, int firstVisibleLine, int visibleLineCount)
        : base(session)
    {
        FirstVisibleLine = firstVisibleLine;
        VisibleLineCount = visibleLineCount;
    }

    public int FirstVisibleLine { get; }
    public int VisibleLineCount { get; }
}
