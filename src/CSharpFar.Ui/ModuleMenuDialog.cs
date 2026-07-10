using CSharpFar.Console;

namespace CSharpFar.Ui;

public sealed class ModuleMenuDialog
{
    private readonly ModalDialogHost _modalDialogs;

    public ModuleMenuDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public int? Show(string title, IReadOnlyList<string> items, int selected)
    {
        if (items.Count == 0)
            return null;

        var dialog = new SelectionListDialog<string>(items, static item => item, title)
        {
            SelectedIndex = Math.Clamp(selected, 0, items.Count - 1),
            MaxVisibleRows = 10,
        };
        var result = dialog.Show(_modalDialogs);
        return result.IsConfirmed ? result.SelectedIndex : null;
    }
}
