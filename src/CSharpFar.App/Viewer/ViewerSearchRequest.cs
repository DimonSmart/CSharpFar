namespace CSharpFar.App.Viewer;

internal sealed record ViewerSearchRequest(
    string Pattern,
    bool CaseSensitive,
    bool WholeWords,
    bool UseRegex,
    bool SearchHex)
{
    public static ViewerSearchRequest FromDialog(ViewerFindDialogResult result) =>
        new(
            result.Pattern,
            result.CaseSensitive,
            result.WholeWords,
            result.UseRegex,
            result.SearchHex);
}
