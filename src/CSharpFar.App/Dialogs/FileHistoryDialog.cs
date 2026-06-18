using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the file history list (most recently accessed first).
/// Returns the selected file path, or null if the user pressed Escape.
/// </summary>
internal sealed class FileHistoryDialog
{
    private readonly ScreenRenderer _screen;

    public FileHistoryDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public string? Show(IReadOnlyList<FileHistoryItem> history)
    {
        if (history.Count == 0)
            return null;

        var paths = history.Select(item => item.Path).Reverse().ToList();
        var dialog = new SelectionListDialog<string>(paths, static path => path, "File History")
        {
            MaxWidth = 60,
            MaxVisibleRows = 15,
        };
        var result = dialog.Show(_screen);
        return result.IsConfirmed ? result.SelectedItem : null;
    }
}
