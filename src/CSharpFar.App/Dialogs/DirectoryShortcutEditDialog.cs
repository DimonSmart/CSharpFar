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

    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DirectoryShortcutEditDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
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

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, DirectoryShortcutEditResult>(
            (context, focusScope) => Draw(context, focusScope, form, number),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
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
                    return ModalDialogLoopResult<DirectoryShortcutEditResult>.Continue;
                }

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    return ModalDialogLoopResult<DirectoryShortcutEditResult>.Complete(Accepted(number, name.Text, path.Text));
                }

                return ModalDialogLoopResult<DirectoryShortcutEditResult>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private ScrollableFormFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        int number)
    {
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(
            context.Canvas,
            _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight),
            $"Directory shortcut {number}",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, layout) =>
            {
                Rect content = layout.ContentBounds;
                var bodyBounds = new Rect(content.X, content.Y, content.Width, Math.Max(1, content.Height - 2));
                var footerBounds = new Rect(content.X, content.Bottom - 1, content.Width, 1);
                frame = form.Render(
                    new FormRenderContext(context, bodyBounds, PaletteStyles.DialogFill(_palette), footerBounds),
                    focusScope);
            });

        return frame ?? throw new InvalidOperationException("Directory shortcut edit dialog did not render a form frame.");
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
