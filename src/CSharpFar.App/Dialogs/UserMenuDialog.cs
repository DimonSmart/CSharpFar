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
    private readonly ModalDialogHost _modalDialogs;

    public UserMenuDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    [Obsolete("Use the ModalDialogHost constructor.")]
    public UserMenuDialog(ScreenRenderer screen) : this(ModalDialogHost.For(screen)) { }

    public string? Show(IReadOnlyList<UserMenuItem> items)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<UserMenuItem>(items, static item => item.Title, "User Menu")
        {
            MaxWidth = 60,
            MaxVisibleRows = 15,
        };
        var result = dialog.Show(_modalDialogs);
        return result.IsConfirmed ? result.SelectedItem?.Command : null;
    }
}
