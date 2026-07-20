using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record DirectoryShortcutsDialogResult(
    bool Changed,
    IReadOnlyList<AppSettings.DirectoryShortcutItem> Items);

internal sealed class DirectoryShortcutsDialog
{
    private static readonly UiTargetId ListTarget = new("directory-shortcuts.list");
    private const int DialogWidth = 68;
    private const int DialogHeight = 16;

    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();
    public DirectoryShortcutsDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _screen = modalDialogs.Screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public DirectoryShortcutsDialogResult Show(
        IReadOnlyList<AppSettings.DirectoryShortcutItem> currentItems,
        string activePanelPath)
    {
        var items = currentItems.ToDictionary(item => item.Number);
        var initialItems = CloneItems(items);
        int cursor = 0;
        var buttons = new ButtonRow([
            new DialogButton("edit", "Edit", 'E', IsDefault: true),
            new DialogButton("close", "Close", 'C'),
        ], PaletteStyles.DialogFill(_palette), PaletteStyles.InputField(_palette))
        { Id = "actions" };
        var form = new ScrollableFormDialog([buttons]);

        return _modalDialogs.RunInteractive<DirectoryShortcutsFrame, DirectoryShortcutsInput, DirectoryShortcutsDialogResult>(
            (context, focusScope) => Draw(context, focusScope, form, items, cursor),
            BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (route.Target == ListTarget)
                    return (new DirectoryShortcutsInput(input, FormInputResult.NotHandled), UiInputResult.HandledResult);

                FormRouteResult result = form.RouteInput(input, frame.Buttons, route);
                return (new DirectoryShortcutsInput(input, result.FormResult), result.UiResult);
            },
            (routed, semantic) =>
            {
                DirectoryShortcutsFrame frame = routed.Frame;
                ConsoleInputEvent input = semantic.Input;
                if (input is MouseConsoleInputEvent mouse &&
                    TrySelectRow(mouse, frame.Layout.ContentBounds, ref cursor))
                {
                    if (mouse.Kind == MouseEventKind.DoubleClick)
                        EditSelected(items, cursor, activePanelPath);
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
                }

                if (semantic.FormResult.Command is string buttonId)
                {
                    if (buttonId == "close")
                        return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));
                    if (buttonId == "edit")
                        EditSelected(items, cursor, activePanelPath);
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        cursor = Math.Max(0, cursor - 1);
                        break;
                    case ConsoleKey.DownArrow:
                        cursor = Math.Min(DirectoryShortcutNormalizer.DisplayOrder.Count - 1, cursor + 1);
                        break;
                    case ConsoleKey.Home:
                        cursor = 0;
                        break;
                    case ConsoleKey.End:
                        cursor = DirectoryShortcutNormalizer.DisplayOrder.Count - 1;
                        break;
                    case ConsoleKey.Enter:
                        EditSelected(items, cursor, activePanelPath);
                        break;
                    case ConsoleKey.Escape:
                    case ConsoleKey.F10:
                        return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));
                }

                return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
            });
    }

    private static UiInteractionFrame BuildInteractionFrame(DirectoryShortcutsFrame frame)
    {
        Rect listBounds = frame.Layout.ContentBounds with
        {
            Height = Math.Min(frame.Layout.ContentBounds.Height, DirectoryShortcutNormalizer.DisplayOrder.Count),
        };
        UiInteractionFrame buttons = frame.Form.BuildInteractionFrame(frame.Buttons);
        return new UiInteractionFrame(
            [new UiHitRegion(ListTarget, listBounds), .. buttons.HitRegions],
            buttons.Focus,
            buttons.KeyboardTarget);
    }

    private DirectoryShortcutsFrame Draw(
        UiRenderContext context,
        UiFocusScope focusScope,
        ScrollableFormDialog form,
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items,
        int cursor)
    {
        Rect outerBounds = _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight);
        ModalDialogRenderer.Layout layout = default;
        ScrollableFormFrame buttons = null!;
        _modalRenderer.Render(
            _screen,
            outerBounds,
            "Directory shortcuts",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, currentLayout) =>
            {
                layout = currentLayout;
                Rect content = currentLayout.ContentBounds;
                for (int row = 0; row < DirectoryShortcutNormalizer.DisplayOrder.Count; row++)
                {
                    int number = DirectoryShortcutNormalizer.DisplayOrder[row];
                    items.TryGetValue(number, out var item);
                    string text = $"{number}  {item?.Name ?? string.Empty,-8}  {item?.Path ?? string.Empty}";
                    _screen.Write(
                        content.X,
                        content.Y + row,
                        Fit(text, content.Width),
                        row == cursor ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette));
                }

                buttons = form.Render(
                    new FormRenderContext(
                        context,
                        new Rect(content.X, content.Y + 11, content.Width, 1),
                        PaletteStyles.DialogBorder(_palette)),
                    focusScope);
            });
        return new DirectoryShortcutsFrame(layout, buttons, form);
    }

    private void EditSelected(
        IDictionary<int, AppSettings.DirectoryShortcutItem> items,
        int cursor,
        string activePanelPath)
    {
        int number = DirectoryShortcutNormalizer.DisplayOrder[cursor];
        items.TryGetValue(number, out var currentItem);
        var result = new DirectoryShortcutEditDialog(_modalDialogs, _palette)
            .Show(number, currentItem, activePanelPath);
        if (!result.Accepted)
            return;

        if (result.Item is null)
            items.Remove(number);
        else
            items[number] = result.Item;
    }

    private static bool TrySelectRow(MouseConsoleInputEvent mouse, Rect contentBounds, ref int cursor)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.DoubleClick) ||
            mouse.X < contentBounds.X ||
            mouse.X >= contentBounds.Right ||
            mouse.Y < contentBounds.Y ||
            mouse.Y >= contentBounds.Y + DirectoryShortcutNormalizer.DisplayOrder.Count)
        {
            return false;
        }

        cursor = mouse.Y - contentBounds.Y;
        return true;
    }

    private static DirectoryShortcutsDialogResult Result(
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> initialItems,
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items)
    {
        var normalizedItems = CloneItems(items);
        bool changed = initialItems.Count != normalizedItems.Count ||
            initialItems.Any(pair =>
                !normalizedItems.TryGetValue(pair.Key, out var item) ||
                pair.Value.Name != item.Name ||
                pair.Value.Path != item.Path);
        return new DirectoryShortcutsDialogResult(
            changed,
            DirectoryShortcutNormalizer.DisplayOrder
                .Where(normalizedItems.ContainsKey)
                .Select(number => normalizedItems[number])
                .ToArray());
    }

    private static Dictionary<int, AppSettings.DirectoryShortcutItem> CloneItems(
        IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items) =>
        items.ToDictionary(
            pair => pair.Key,
            pair => new AppSettings.DirectoryShortcutItem
            {
                Number = pair.Value.Number,
                Name = pair.Value.Name,
                Path = pair.Value.Path,
            });

    private static string Fit(string text, int width) =>
        text.Length <= width ? text.PadRight(width) : text[..width];

    private readonly record struct DirectoryShortcutsFrame(
        ModalDialogRenderer.Layout Layout,
        ScrollableFormFrame Buttons,
        ScrollableFormDialog Form);

    private readonly record struct DirectoryShortcutsInput(
        ConsoleInputEvent Input,
        FormInputResult FormResult);
}
