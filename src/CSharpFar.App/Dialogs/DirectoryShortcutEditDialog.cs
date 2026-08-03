using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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

    private readonly ModalFormHost _formDialogs;
    private readonly ConsolePalette _palette;
    private readonly FormFieldFactory _fields;

    public DirectoryShortcutEditDialog(ModalDialogHost modalDialogs, FormFieldFactory fields, ConsolePalette? palette = null)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _palette = palette ?? PaletteRegistry.Default;
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
        var form = new ScrollableFormDialog();

        void PrepareRows() =>
            form.SetRows(
                [
                    new LabelRow("Name", PaletteStyles.DialogFill(_palette)),
                    nameRow,
                    new SpacerRow(PaletteStyles.DialogFill(_palette)),
                    new LabelRow("Path", PaletteStyles.DialogFill(_palette)),
                    pathRow,
                ],
                [actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                $"Directory shortcut {number}", DialogWidth, DialogHeight,
                OuterRenderOptions: PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
                FrameRenderOptions: PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false }),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 1),
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<DirectoryShortcutEditResult>.Complete(new DirectoryShortcutEditResult(false, currentItem));

                if (result.Kind == FormInputResultKind.NotHandled &&
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter })
                {
                    if (form.FocusedRowId == "name")
                        return ModalDialogLoopResult<DirectoryShortcutEditResult>.ContinueWithFocus(
                            form.GetFocusTarget("path"));
                    else if (form.FocusedRowId == "path")
                        return ModalDialogLoopResult<DirectoryShortcutEditResult>.ContinueWithFocus(
                            form.GetFocusTarget("actions"));
                    return ModalDialogLoopResult<DirectoryShortcutEditResult>.ContinueNoChange;
                }

                if (FormDialogInput.ShouldSubmit(routed, result, form))
                {
                    return ModalDialogLoopResult<DirectoryShortcutEditResult>.Complete(Accepted(number, name.Text, path.Text));
                }

                return ModalDialogLoopResult<DirectoryShortcutEditResult>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
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
