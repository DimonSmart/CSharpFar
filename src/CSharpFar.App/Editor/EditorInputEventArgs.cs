namespace CSharpFar.App.Editor;

public sealed class EditorInputEventArgs : EditorSessionEventArgs
{
    public EditorInputEventArgs(EditorSession session, ConsoleKeyInfo key)
        : base(session)
    {
        Key = key;
    }

    public ConsoleKeyInfo Key { get; }
}
