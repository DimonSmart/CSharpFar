using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Shows the user-menu list.
/// Enter returns the selected command; Escape returns null.
/// </summary>
internal sealed class UserMenuDialog
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public UserMenuDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public string? Show(IReadOnlyList<UserMenuItem> items)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<UserMenuItem>(items, static item => item.Title, "User Menu")
        {
            MaxWidth = 60,
            MaxVisibleRows = 15,
        };
        var result = dialog.Show(_screen, _palette);
        return result.IsConfirmed ? result.SelectedItem?.Command : null;
    }
}
