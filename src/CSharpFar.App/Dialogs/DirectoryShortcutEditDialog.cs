using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record DirectoryShortcutEditResult(
    bool Accepted,
    AppSettings.DirectoryShortcutItem? Item);

internal sealed class DirectoryShortcutEditDialog
{
    private const int DialogWidth = 62;
    private const int DialogHeight = 10;

    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public DirectoryShortcutEditDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    internal DirectoryShortcutEditDialog(ModalDialogHost modalDialogs, FormFieldFactory fields, ConsolePalette? palette = null)
        : this(new DialogService(modalDialogs, fields), fields)
    {
    }

    public DirectoryShortcutEditResult Show(
        int number,
        AppSettings.DirectoryShortcutItem? currentItem,
        string activePanelPath)
    {
        TextField name = _fields.Text("name", currentItem?.Name ?? DirectoryShortcutNormalizer.GetDefaultNameFromPath(activePanelPath));
        TextField path = _fields.Text("path", currentItem?.Path ?? activePanelPath);
        TextInputRow nameRow = FormControls.Text(name);
        TextInputRow pathRow = FormControls.Text(path);
        var actions = FormControls.Buttons(
            "actions",
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel());
        return _dialogs.Form(
            new FormDialogOptions($"Directory shortcut {number}", DialogWidth, DialogHeight),
            rows: () =>
            [
                FormControls.Label("Name"),
                nameRow,
                FormControls.Spacer(),
                FormControls.Label("Path"),
                pathRow,
            ],
            footer: () => [actions],
            (result) =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<DirectoryShortcutEditResult>.Complete(new DirectoryShortcutEditResult(false, currentItem));

                if (result.Kind == FormDialogEventKind.NotHandled && result.Key == ConsoleKey.Enter)
                {
                    if (result.FocusedRowId == "name")
                        return FormDialogOutcome<DirectoryShortcutEditResult>.ContinueWithFocus("path");
                    if (result.FocusedRowId == "path")
                        return FormDialogOutcome<DirectoryShortcutEditResult>.ContinueWithFocus("actions");
                    return FormDialogOutcome<DirectoryShortcutEditResult>.Continue();
                }

                if (result.IsSubmitted)
                    return FormDialogOutcome<DirectoryShortcutEditResult>.Complete(Accepted(number, name.Text, path.Text));

                return FormDialogOutcome<DirectoryShortcutEditResult>.Continue();
            });
    }

    private static DirectoryShortcutEditResult Accepted(int number, string name, string path)
    {
        path = path.Trim();
        return new DirectoryShortcutEditResult(
            true,
            path.Length == 0
                ? null
                : new AppSettings.DirectoryShortcutItem
                {
                    Number = number,
                    Name = DirectoryShortcutNormalizer.NormalizeName(name),
                    Path = path,
                });
    }

}
