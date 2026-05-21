namespace CSharpFar.App.Editor;

public sealed class EditorSyntaxHighlightResult
{
    public EditorSyntaxHighlightResult(
        IReadOnlyList<EditorColorSpan> spans,
        EditorSyntaxDiagnostics diagnostics)
    {
        Spans = spans;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<EditorColorSpan> Spans { get; }
    public EditorSyntaxDiagnostics Diagnostics { get; }

    public static EditorSyntaxHighlightResult Plain(string reason) =>
        new([], EditorSyntaxDiagnostics.Plain(reason));

    public static EditorSyntaxHighlightResult Disabled(string reason) =>
        new([], EditorSyntaxDiagnostics.Disabled(reason));
}
