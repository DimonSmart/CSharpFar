namespace CSharpFar.App.Editor;

public sealed record EditorTextChange(
    EditorPosition Start,
    EditorPosition End,
    string OldText,
    string NewText);
