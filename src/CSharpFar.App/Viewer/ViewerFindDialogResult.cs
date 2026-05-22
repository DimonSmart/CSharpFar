namespace CSharpFar.App.Viewer;

internal sealed record ViewerFindDialogResult(
    string Pattern,
    bool CaseSensitive,
    bool WholeWords,
    bool UseRegex,
    bool SearchHex);
