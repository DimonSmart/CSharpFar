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
        var targets = new UiTargetScope("directory-shortcuts");
        var routedShortcuts = new RoutedScrollableList<int>(
            DirectoryShortcutNormalizer.DisplayOrder,
            number => FormatShortcut(number, items),
            targets.Child("list"),
            targets.Child("list.scrollbar"))
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
            (context, focusScope) => Draw(context, focusScope, form, routedShortcuts),
            frame => BuildInteractionFrame(frame, routedShortcuts),
            (input, frame, route) => RouteInput(input, frame, route, form, routedShortcuts),
            (routed, semantic) =>
            {
                if (semantic.FormResult.Command is string buttonId)
                {
                    if (buttonId == "close")
                        return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));
                    if (buttonId == "edit")
                        EditSelected(items, routedShortcuts.SelectedItemOrDefault, activePanelPath);
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
                }

                if (semantic.ListResult.Kind == ScrollableListInputResultKind.Confirmed)
                    EditSelected(items, routedShortcuts.SelectedItemOrDefault, activePanelPath);

                if (semantic.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Complete(Result(initialItems, items));

                return ModalDialogLoopResult<DirectoryShortcutsDialogResult>.Continue;
            },
            applyCommittedFrame: frame => routedShortcuts.ApplyCommittedFrame(frame.ListState));
    }

    private static UiInteractionFrame BuildInteractionFrame(
        DirectoryShortcutsFrame frame,
        RoutedScrollableList<int> shortcuts)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(shortcuts.BuildInteractionFragment(
                frame.ListBounds,
                frame.ListState,
                0,
                frame.ListBounds.Width > 0 && frame.ListBounds.Height > 0))
            .AddFragment(frame.Form.BuildInteractionFragment(frame.Buttons))
            .SetDefaultFocusTarget(frame.ListBounds.Width > 0 && frame.ListBounds.Height > 0 ? shortcuts.ListTarget : frame.Buttons.DefaultTarget);
        return builder.Build();
    }

    private static (DirectoryShortcutsInput Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        DirectoryShortcutsFrame frame,
        UiInputRouteContext route,
        ScrollableFormDialog form,
        RoutedScrollableList<int> shortcuts)
    {
        bool isListRoute = shortcuts.IsTargetRoute(route);
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

        RoutedScrollableListInputResult routedResult = shortcuts.RouteInput(input, frame.ListBounds, frame.ListState, route);
        if (!routedResult.ListResult.IsHandled && UiFocusRouting.TryHandleTraversal(input, out UiInputResult focusResult))
            return (new DirectoryShortcutsInput(input, FormInputResult.NotHandled, routedResult.ListResult), focusResult);
        return (
            new DirectoryShortcutsInput(input, FormInputResult.NotHandled, routedResult.ListResult),
            routedResult.UiResult);
    }

    private DirectoryShortcutsFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        ScrollableFormDialog form,
        RoutedScrollableList<int> routedShortcuts)
    {
        DirectoryShortcutsLayout layout = CalculateLayout(_modalRenderer.CalculateLayout(context.Size, DialogWidth, DialogHeight));
        ScrollableFormFrame buttons = null!;
        ScrollableListFrameState listState = routedShortcuts.CalculateFrame(
            layout.ListBounds.Height,
            layout.ScrollbarBounds);
        _modalRenderer.Render(
            context.Canvas,
            layout.Modal,
            "Directory shortcuts",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, _) =>
            {
                routedShortcuts.Render(context.Canvas, layout.ListBounds, listState);
                routedShortcuts.RenderScrollbar(context.Canvas, listState, PaletteStyles.DialogBorder(_palette));
                buttons = layout.FooterBounds.Height > 0
                    ? form.Render(
                        new FormRenderContext(
                            context,
                            layout.FormBodyBounds,
                            PaletteStyles.DialogBorder(_palette),
                            layout.FooterBounds),
                        focusScope,
                        [new UiFocusEntry(routedShortcuts.ListTarget, 0)],
                        routedShortcuts.ListTarget)
                    : EmptyFormFrame(context, layout.FormBodyBounds);
            });
        return new DirectoryShortcutsFrame(layout.Modal, layout.ListBounds, listState, buttons, form);
    }

    private static DirectoryShortcutsLayout CalculateLayout(ModalDialogRenderer.Layout modal)
    {
        Rect content = modal.ContentBounds;
        int footerY = content.Y + Math.Min(11, Math.Max(0, content.Height - 1));
        Rect listBounds = new(content.X, content.Y, content.Width, Math.Max(0, footerY - content.Y - 1));
        Rect formBodyBounds = new(content.X, Math.Clamp(footerY - 1, content.Y, content.Bottom), content.Width, footerY > content.Y ? 1 : 0);
        Rect footerBounds = new(content.X, footerY, content.Width, footerY < content.Bottom ? 1 : 0);
        Rect? scrollbarBounds = listBounds.Width > 0 && listBounds.Height > 0 &&
            DirectoryShortcutNormalizer.DisplayOrder.Count > listBounds.Height
            ? new Rect(content.Right - 1, listBounds.Y, 1, listBounds.Height)
            : null;
        return new DirectoryShortcutsLayout(modal, listBounds, scrollbarBounds, formBodyBounds, footerBounds);
    }

    private static ScrollableFormFrame EmptyFormFrame(UiRenderContext context, Rect bodyBounds) =>
        new(context.Viewport, bodyBounds, null, 0, context.Viewport.Height, 0, [], null);

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

    private readonly record struct DirectoryShortcutsLayout(
        ModalDialogRenderer.Layout Modal,
        Rect ListBounds,
        Rect? ScrollbarBounds,
        Rect FormBodyBounds,
        Rect FooterBounds);

    private readonly record struct DirectoryShortcutsInput(
        ConsoleInputEvent Input,
        FormInputResult FormResult,
        ScrollableListInputResult ListResult);
}
