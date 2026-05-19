namespace CSharpFar.App.Editor;

public class EditorSessionEventArgs : EventArgs
{
    public EditorSessionEventArgs(EditorSession session)
    {
        Session = session;
    }

    public EditorSession Session { get; }
}
