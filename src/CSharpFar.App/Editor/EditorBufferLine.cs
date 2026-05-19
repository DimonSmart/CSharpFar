namespace CSharpFar.App.Editor;

internal sealed class EditorBufferLine
{
    public EditorBufferLine(string text, string? ending)
    {
        Text = text;
        Ending = ending;
    }

    public string Text { get; set; }
    public string? Ending { get; set; }
}
