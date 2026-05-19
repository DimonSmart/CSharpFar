namespace CSharpFar.App.Editor;

public sealed record EditorSearchOptions(
    string Pattern,
    bool SearchBackward = false,
    bool CaseSensitive = false,
    bool WholeWords = false,
    bool UseRegex = false);
