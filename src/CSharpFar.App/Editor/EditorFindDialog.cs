using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal sealed class EditorFindDialog
{
    private const string CaseSensitiveOption = "case-sensitive";
    private const string WholeWordsOption = "whole-words";

    private readonly ModalDialogHost _modalDialogs;

    public EditorFindDialog(ModalDialogHost modalDialogs, ConsolePalette palette)
    {
        _modalDialogs = modalDialogs;
        _ = palette;
    }

    public EditorFindDialogResult? Show(EditorFindDialogResult? previous)
    {
        var result = new SearchOptionsDialog(_modalDialogs).Show(new SearchOptionsDialogOptions
        {
            Title = "Find",
            InitialPattern = previous?.Pattern ?? string.Empty,
            HistoryKey = "Editor.Find.Pattern",
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
