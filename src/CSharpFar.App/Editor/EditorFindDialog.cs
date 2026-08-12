using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFindDialog
{
    private const string CaseSensitiveOption = "case-sensitive";
    private const string WholeWordsOption = "whole-words";

    private readonly DialogService _dialogs;

    public EditorFindDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public EditorFindDialogResult? Show(EditorFindDialogResult? previous)
    {
        var result = _dialogs.SearchOptions(new SearchOptionsDialogOptions
        {
            Title = "Find",
            InitialPattern = previous?.Pattern ?? string.Empty,
            History = AppTextHistoryIds.EditorFindPattern,
            Width = 56,
            Options =
            [
                new SearchOptionLine(CaseSensitiveOption, "CaseSensitive", previous?.CaseSensitive ?? false),
                new SearchOptionLine(WholeWordsOption, "WholeWords", previous?.WholeWords ?? false),
            ],
        });

        return result is null
            ? null
            : new EditorFindDialogResult(
                result.Pattern,
                result.GetOption(CaseSensitiveOption),
                result.GetOption(WholeWordsOption));
    }
}
