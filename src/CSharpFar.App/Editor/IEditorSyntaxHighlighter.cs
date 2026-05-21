namespace CSharpFar.App.Editor;

public interface IEditorSyntaxHighlighter
{
    EditorSyntaxHighlightResult Highlight(EditorSyntaxHighlightRequest request);
}
