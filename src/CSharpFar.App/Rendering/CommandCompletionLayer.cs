using CSharpFar.App.CommandLine;
using CSharpFar.App.Input;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record CommandCompletionItemFrame(int AbsoluteIndex, string Text, Rect Bounds);

internal sealed record CommandCompletionFrame(
    bool Visible,
    ConsoleViewport Viewport,
    Rect PopupBounds,
    Rect ContentBounds,
    IReadOnlyList<CommandCompletionItemFrame> Items,
    Rect? ScrollbarBounds,
    int VisibleRows,
    int MatchCount,
    RoutedScrollableListFrame List);

internal sealed class CommandCompletionLayer : UiLayer<CommandCompletionFrame>
{
    private static readonly UiTargetScope Targets = new("application.command-completion");
    private static readonly UiTargetId ListTarget = Targets.Child("list");
    private static readonly UiTargetId ScrollbarTarget = Targets.Child("list.scrollbar");
    private readonly ApplicationRenderContext _context;
    private readonly CommandCompletionController _controller;
    private readonly Action<bool> _hideCompletion;
    private readonly Action _resetHistoryNavigation;
    private readonly PopupRenderer _popupRenderer = new();
    private readonly RoutedScrollableList<string> _list;

    public CommandCompletionLayer(ApplicationRenderContext context, CommandCompletionController controller, Action<bool> hideCompletion, Action resetHistoryNavigation)
    {
        _context = context;
        _controller = controller;
        _hideCompletion = hideCompletion;
        _resetHistoryNavigation = resetHistoryNavigation;
        _list = new RoutedScrollableList<string>(
            context.CommandCompletion.List,
            ListTarget,
            ScrollbarTarget,
            new RoutedScrollableListInteractionOptions
            {
                AcceptKeyboardFromLayerRoute = true,
            });
    }

    public override UiLayerInputPolicy InputPolicy => HasCommittedFrame && CommittedFrame.Visible ? UiLayerInputPolicy.Bubble : UiLayerInputPolicy.None;

    protected override CommandCompletionFrame RenderFrame(UiRenderContext context)
    {
        var completion = _context.CommandCompletion;
        var list = _list;
        var empty = new CommandCompletionFrame(false, context.Viewport, default, default, [], null, 0, list.Count, new RoutedScrollableListFrame(default, ScrollableListFrameState.Empty));
        if (_context.App.WorkspaceMode != ApplicationWorkspaceMode.Panels)
            return empty;

        int availableRows = CommandCompletionLayout.VisibleRows(context.Size);
        if (!completion.Visible || !list.HasItems || availableRows <= 0)
            return empty;

        int rowCount = Math.Min(availableRows, list.Count);
        int height = rowCount + 2;
        int commandLineRow = ApplicationLayoutService.CommandLineRow(context.Size);
        var popupBounds = new Rect(0, commandLineRow - height, context.Size.Width, height);
        var contentBounds = new Rect(1, popupBounds.Y + 1, Math.Max(0, popupBounds.Width - 2), rowCount);
        var candidateScrollbarBounds = new Rect(popupBounds.Right - 1, popupBounds.Y + 1, 1, rowCount);
        RoutedScrollableListFrame candidate = list.CalculateFrame(rowCount, contentBounds, candidateScrollbarBounds);
        ScrollState? scrollState = list.GetScrollState(rowCount, candidate.List.ScrollTop);
        Rect? scrollbarBounds = scrollState is not null && ScrollBarInteraction.IsInteractive(candidateScrollbarBounds, scrollState)
            ? candidateScrollbarBounds
            : null;
        RoutedScrollableListFrame listFrame = list.CalculateFrame(rowCount, contentBounds, scrollbarBounds);

        var popupOptions = PaletteStyles.DialogPopupOptions(_context.App.Palette) with
        {
            DrawShadow = false,
            VerticalScrollState = list.GetScrollState(rowCount, listFrame.List.ScrollTop),
        };
        _popupRenderer.RenderPopup(context.Canvas, popupBounds, popupOptions, (screen, bounds) =>
            list.Render(screen, listFrame, PaletteStyles.DialogFill(_context.App.Palette), PaletteStyles.InputField(_context.App.Palette), PaletteStyles.DialogFill(_context.App.Palette)));

        var items = Enumerable.Range(0, rowCount).Select(row =>
        {
            int index = listFrame.List.ScrollTop + row;
            return new CommandCompletionItemFrame(index, list.Items[index], new Rect(contentBounds.X, contentBounds.Y + row, contentBounds.Width, 1));
        }).ToArray();
        return new CommandCompletionFrame(true, context.Viewport, popupBounds, contentBounds, items, scrollbarBounds, rowCount, list.Count, listFrame);
    }

    protected override void OnFrameCommitted(CommandCompletionFrame frame)
    {
        if (!frame.Visible)
        {
            _list.ApplyCommittedFrame(new RoutedScrollableListFrame(default, ScrollableListFrameState.Empty));
            return;
        }

        if (_context.CommandCompletion.Visible && _list.Count == frame.MatchCount)
            _list.ApplyCommittedFrame(frame.List);
    }

    protected override UiInteractionFrame BuildInteractionFrame(CommandCompletionFrame frame)
    {
        if (!frame.Visible)
            return UiInteractionFrame.Empty;
        return new UiInteractionFrameBuilder()
            .AddFragment(_list.BuildInteractionFragment(frame.List, tabOrder: 0))
            .Build();
    }

    protected override UiInputResult RouteInput(ConsoleInputEvent input, CommandCompletionFrame frame, UiInputRouteContext context) =>
        !frame.Visible || frame.VisibleRows <= 0 || frame.MatchCount == 0 ? UiInputResult.NotHandled : input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame, context),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };

    private UiInputResult RouteKey(ConsoleKeyInfo key, CommandCompletionFrame frame, UiInputRouteContext route)
    {
        if (!TryRestoreCommittedList(frame))
            return UiInputResult.HandledAndInvalidate;

        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            return _list.RouteInput(new KeyConsoleInputEvent(key), frame.List, route).UiResult;
        if (key.Key == ConsoleKey.Enter)
            return KeyboardShortcutClassifier.IsPlainControlEnter(key) ? UiInputResult.NotHandled : AcceptByKeyboard(frame);
        if (key.Key == ConsoleKey.Escape)
        {
            _hideCompletion(true);
            return UiInputResult.HandledAndInvalidate;
        }
        if (key.Key == ConsoleKey.Delete && _controller.TryRemoveSelectedCommand(_context.CommandLine, frame.VisibleRows))
        {
            _resetHistoryNavigation();
            return UiInputResult.HandledAndInvalidate;
        }
        return UiInputResult.NotHandled;
    }

    private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, CommandCompletionFrame frame, UiInputRouteContext route)
    {
        if (!TryRestoreCommittedList(frame))
            return UiInputResult.HandledAndInvalidate;

        RoutedScrollableListInputResult routed = _list.RouteInput(
            mouse,
            frame.List,
            route,
            confirmOnMouseDown: true,
            confirmOnDoubleClick: true);
        ScrollableListInputResult result = routed.ListResult;
        if (!result.IsHandled)
            return UiInputResult.NotHandled;
        if (result.Kind == ScrollableListInputResultKind.Confirmed)
            return AcceptByMouse(_list.SelectedIndex, frame);
        return routed.UiResult;
    }

    private UiInputResult AcceptByKeyboard(CommandCompletionFrame frame) =>
        AcceptItem(frame.List.List.SelectedIndex, frame, continueRoutingForNeutralItem: true);

    private UiInputResult AcceptByMouse(int itemIndex, CommandCompletionFrame frame) =>
        AcceptItem(itemIndex, frame, continueRoutingForNeutralItem: false);

    private UiInputResult AcceptItem(int itemIndex, CommandCompletionFrame frame, bool continueRoutingForNeutralItem)
    {
        var completion = _context.CommandCompletion;
        if (!TryGetCommittedItem(itemIndex, frame, out var item) || itemIndex >= completion.List.Count || !string.Equals(completion.List.Items[itemIndex], item.Text, StringComparison.Ordinal))
            return UiInputResult.NotHandled;
        if (itemIndex == 0)
        {
            _hideCompletion(false);
            _resetHistoryNavigation();
            return continueRoutingForNeutralItem ? UiInputResult.NotHandled : UiInputResult.HandledAndInvalidate;
        }
        _context.CommandLine.SetText(item.Text);
        _hideCompletion(false);
        _resetHistoryNavigation();
        return UiInputResult.HandledAndInvalidate;
    }

    private bool TryRestoreCommittedList(CommandCompletionFrame frame)
    {
        var completion = _context.CommandCompletion;
        if (!completion.Visible ||
            completion.List.Count != frame.MatchCount ||
            frame.Items.Any(item => item.AbsoluteIndex >= completion.List.Count || !string.Equals(completion.List.Items[item.AbsoluteIndex], item.Text, StringComparison.Ordinal)))
            return false;

        _list.ApplyCommittedFrame(frame.List);
        return true;
    }

    private static bool TryGetCommittedItem(int index, CommandCompletionFrame frame, out CommandCompletionItemFrame item)
    {
        item = frame.Items.FirstOrDefault(candidate => candidate.AbsoluteIndex == index)!;
        return item is not null;
    }
}
