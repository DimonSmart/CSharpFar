using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
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
    private static readonly UiTargetId ScrollbarTarget = new("directory-shortcuts.list.scrollbar");
    private const int DialogWidth = 68;
    private const int DialogHeight = 16;

    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DirectoryShortcutsDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public DirectoryShortcutsDialogResult Show(
        IReadOnlyList<AppSettings.DirectoryShortcutItem> currentItems,
        string activePanelPath)
    {
        var items = currentItems.ToDictionary(item => item.Number);
        var initialItems = CloneItems(items);
        var shortcuts = new ScrollableList<int>(DirectoryShortcutNormalizer.DisplayOrder, number => FormatShortcut(number, items))
        {
            NormalStyle = PaletteStyles.DialogFill(_palette),
            SelectedStyle = PaletteStyles.InputField(_palette),
        };
        var buttons = new ButtonRow(
        [
            new DialogButton("edit", "Edit", 'E', IsDefault: true),
            new DialogButton("close", "Close", 'C'),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        form.SetRows([], [buttons]);

        return _modalDialogs.RunInteractive<DirectoryShortcutsFrame, DirectoryShortcutsInput, DirectoryShortcutsDialogResult>(
            (context, focusScope) => Draw(context, focusScope, form, shortcuts),
            BuildInteractionFrame,
            (input, frame, route) => RouteInput(input, frame, route, form, shortcuts),
            (routed, semantic) =>
            {
                if (semantic.FormResult.Command is string buttonId)
                {
                    if (buttonId == "close")
                        return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));
                    if (buttonId == "edit")
                        EditSelected(items, shortcuts.SelectedItemOrDefault, activePanelPath);
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
                }

                if (semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
                    EditSelected(items, shortcuts.SelectedItemOrDefault, activePanelPath);

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));

                return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
            },
            applyCommittedFrame: frame => shortcuts.ApplyCommittedFrame(frame.ListState));
    }

    private static UiInteractionFrame BuildInteractionFrame(DirectoryShortcutsFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddHitRegion(ListTarget, frame.ListBounds)
            .AddFocusEntry(ListTarget, 0)
            .AddFragment(frame.Form.BuildInteractionFragment(frame.Buttons))
            .SetDefaultFocusTarget(ListTarget);
        if (frame.ListState.ScrollbarBounds is Rect scrollbarBounds)
            builder.AddHitRegion(ScrollbarTarget, scrollbarBounds);
        return builder.Build();
    }

    private static (DirectoryShortcutsInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        DirectoryShortcutsFrame frame,
        UiInputRouteContext route,
        ScrollableFormDialog form,
        ScrollableList<int> shortcuts)
    {
        shortcuts.ApplyCommittedFrame(frame.ListState);
        if (input is KeyConsoleInputEvent { Key: var key } && TryRouteFocusKey(key, frame, route, out UiInputResult focusResult))
            return (new DirectoryShortcutsInput(input, FormInputResult.NotHandled, ScrollableListInputResult.NotHandled), focusResult);

        bool isListRoute = route.Target == ListTarget || route.Target == ScrollbarTarget;
        if (!isListRoute)
        {
            FormRouteResult formResult = form.RouteInput(input, frame.Buttons, route, allowUnfocusedButtonHotkeys: true);
            return (new DirectoryShortcutsInput(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
        }

        if (input is KeyConsoleInputEvent { Key.KeyChar: > ' ' } keyInput)
        {
            FormRouteResult formResult = form.RouteInput(keyInput, frame.Buttons, route, allowUnfocusedButtonHotkeys: true);
            if (formResult.FormResult.IsHandled)
                return (new DirectoryShortcutsInput(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
        }

        ScrollableListInputResult listResult = input switch
        {
            KeyConsoleInputEvent { Key: var listKey } => shortcuts.HandleKey(listKey, frame.ListState.ViewportRows),
            MouseConsoleInputEvent mouse => shortcuts.HandleMouse(mouse, frame.ListBounds, frame.ListState),
            _ => ScrollableListInputResult.NotHandled,
        };
        return (
            new DirectoryShortcutsInput(input, FormInputResult.NotHandled, listResult),
            ToUiInputResult(listResult, ScrollbarTarget));
    }

    private DirectoryShortcutsFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        ScrollableList<int> shortcuts)
    {
        Rect outerBounds = _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight);
        ModalDialogRenderer.Layout layout = default;
        ScrollableFormFrame buttons = null!;
        ScrollableListFrameState listState = ScrollableListFrameState.Empty;
        Rect listBounds = default;
        _modalRenderer.Render(
            context.Canvas,
            outerBounds,
            "Directory shortcuts",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, currentLayout) =>
            {
                layout = currentLayout;
                Rect content = currentLayout.ContentBounds;
                int buttonY = content.Y + Math.Min(11, Math.Max(0, content.Height - 1));
                listBounds = new Rect(content.X, content.Y, content.Width, Math.Max(1, buttonY - content.Y - 1));
                Rect scrollbarBounds = new(content.Right - 1, listBounds.Y, 1, listBounds.Height);
                listState = shortcuts.CalculateFrameState(
                    listBounds.Height,
                    shortcuts.Count > listBounds.Height ? scrollbarBounds : null);
                shortcuts.Render(context.Canvas, listBounds, listState);
                if (shortcuts.GetScrollState(listBounds.Height, listState.ScrollTop) is { } scrollState)
                {
                    new ScrollBarRenderer().RenderVerticalScrollbar(
                        context.Canvas,
                        scrollbarBounds,
                        scrollState,
                        new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                        PaletteStyles.DialogBorder(_palette));
                }
                buttons = form.Render(
                    new FormRenderContext(
                        context,
                        new Rect(content.X, buttonY - 1, content.Width, 1),
                        PaletteStyles.DialogBorder(_palette),
                        new Rect(content.X, buttonY, content.Width, 1)),
                    focusScope,
                    [new UiFocusEntry(ListTarget, 0)],
                    ListTarget);
            });
        return new DirectoryShortcutsFrame(layout, listBounds, listState, buttons, form);
    }

    private static bool TryRouteFocusKey(
        ConsoleKeyInfo key,
        DirectoryShortcutsFrame frame,
        UiInputRouteContext route,
        out UiInputResult result)
    {
        if (key.Key != ConsoleKey.Tab)
        {
            result = UiInputResult.NotHandled;
            return false;
        }

        bool reverse = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if (route.Target == ListTarget && frame.Buttons.DefaultTarget is UiTargetId buttonTarget)
        {
            result = UiInputResult.RequestFocus(buttonTarget);
            return true;
        }

        if (route.Target == frame.Buttons.DefaultTarget || reverse)
        {
            result = UiInputResult.RequestFocus(ListTarget);
            return true;
        }

        result = UiInputResult.NotHandled;
        return false;
    }

    private static UiInputResult ToUiInputResult(ScrollableListInputResult result, UiTargetId scrollbarTarget) =>
        result.IsHandled
            ? result.DragStarted
                ? UiInputResult.CaptureMouse(scrollbarTarget, MouseButton.Left, invalidate: true)
                : result.DragEnded
                    ? UiInputResult.ReleaseMouse(invalidate: true)
                    : UiInputResult.HandledAndInvalidate
            : UiInputResult.NotHandled;

    private void EditSelected(
        IDictionary<int, AppSettings.DirectoryShortcutItem> items,
        int? number,
        string activePanelPath)
    {
        if (number is null)
            return;

        items.TryGetValue(number.Value, out var currentItem);
        var result = new DirectoryShortcutEditDialog(_modalDialogs, _palette)
            .Show(number.Value, currentItem, activePanelPath);
        if (!result.Accepted)
            return;

        if (result.Item is null)
            items.Remove(number.Value);
        else
            items[number.Value] = result.Item;
    }

    private static string FormatShortcut(int number, IReadOnlyDictionary<int, AppSettings.DirectoryShortcutItem> items)
    {
        items.TryGetValue(number, out var item);
        return $"{number}  {item?.Name ?? string.Empty,-8}  {item?.Path ?? string.Empty}";
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

    private readonly record struct DirectoryShortcutsFrame(
        ModalDialogRenderer.Layout Layout,
        Rect ListBounds,
        ScrollableListFrameState ListState,
        ScrollableFormFrame Buttons,
        ScrollableFormDialog Form);

    private readonly record struct DirectoryShortcutsInput(
        ConsoleInputEvent Input,
        FormInputResult FormResult,
        ScrollableListInputResult ListResult);
}
