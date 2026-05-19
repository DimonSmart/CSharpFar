namespace CSharpFar.App.Editor;

internal sealed record EditorFindDialogResult(
    string Pattern,
    bool CaseSensitive,
    bool WholeWords);
