using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the command history list (most recent first).
/// Returns the selected command text, or null if the user pressed Escape.
/// </summary>
internal sealed class HistoryDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public HistoryDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public string? Show(IReadOnlyList<CommandHistoryItem> history)
    {
        if (history.Count == 0)
            return null;

        var commands = history.Select(item => item.Command).Reverse().ToList();
        var dialog = new SelectionListDialog<string>(commands, static command => command, "Command History")
        {
            MaxWidth = 60,
            MaxVisibleRows = 15,
        };
        var result = dialog.Show(_screen);
        return result.IsConfirmed ? result.SelectedItem : null;
    }
}
