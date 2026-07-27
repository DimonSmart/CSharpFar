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

    public DirectoryShortcutEditDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _palette = palette ?? PaletteRegistry.Default;
    }

    public DirectoryShortcutEditResult Show(
        int number,
        AppSettings.DirectoryShortcutItem? currentItem,
        string activePanelPath)
    {
        var name = Buffer(currentItem?.Name ?? DirectoryShortcutNormalizer.GetDefaultNameFromPath(activePanelPath));
        var path = Buffer(currentItem?.Path ?? activePanelPath);
        var nameRow = new TextInputRow(name) { Id = "name" };
        var pathRow = new TextInputRow(path) { Id = "path" };
        var actions = new ButtonRow(
            [
                new DialogButton("ok", "OK", 'O', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            ])
        {
            Id = "actions",
        };
        var form = new ScrollableFormDialog();

        void PrepareRows() =>
            form.SetRows(
                [
                    new LabelRow("Name", PaletteStyles.DialogFill(_palette)),
                    nameRow,
                    new SeparatorRow(PaletteStyles.DialogFill(_palette), drawLine: false),
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
            static layout => new ModalFormLayout(
                new Rect(layout.ContentBounds.X, layout.ContentBounds.Y, layout.ContentBounds.Width, Math.Max(1, layout.ContentBounds.Height - 2)),
                new Rect(layout.ContentBounds.X, layout.ContentBounds.Bottom - 1, layout.ContentBounds.Width, 1)),
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
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

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
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

    private static CommandLineState Buffer(string text)
    {
        var buffer = new CommandLineState();
        buffer.SetText(text);
        return buffer;
    }
}
