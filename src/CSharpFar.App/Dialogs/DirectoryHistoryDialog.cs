using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the visited-directory history list (most recent first).
/// Returns the selected path, or null if the user pressed Escape.
/// </summary>
internal sealed class DirectoryHistoryDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public DirectoryHistoryDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public string? Show(IReadOnlyList<DirectoryHistoryItem> history)
    {
        if (history.Count == 0)
            return null;

        var paths = history.Select(item => item.Path).Reverse().ToList();
        var dialog = new SelectionListDialog<string>(paths, static path => path, "Directory History")
        {
            MaxWidth = 60,
            MaxVisibleRows = 15,
        };
        var result = dialog.Show(_screen);
        return result.IsConfirmed ? result.SelectedItem : null;
    }
}
