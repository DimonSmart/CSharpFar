using CSharpFar.App.CommandLine;
using CSharpFar.App.Input;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record CommandCompletionItemFrame(int AbsoluteIndex, string Text, Rect Bounds, UiTargetId Target);

internal sealed record CommandCompletionFrame(
    bool Visible,
    ConsoleViewport Viewport,
    Rect PopupBounds,
    Rect ContentBounds,
    IReadOnlyList<CommandCompletionItemFrame> Items,
    Rect? ScrollbarBounds,
    int VisibleRows,
    int FirstVisibleIndex,
    int SelectedIndex,
    int MatchCount,
    ScrollableListFrameState ListState);

internal sealed class CommandCompletionLayer : UiLayer<CommandCompletionFrame>
{
    private static readonly UiTargetId ScrollbarTarget = new("application.command-completion.scrollbar");
    private readonly ApplicationRenderContext _context;
    private readonly CommandCompletionController _controller;
    private readonly Action<bool> _hideCompletion;
    private readonly Action _resetHistoryNavigation;
    private readonly PopupRenderer _popupRenderer = new();

    public CommandCompletionLayer(ApplicationRenderContext context, CommandCompletionController controller, Action<bool> hideCompletion, Action resetHistoryNavigation)
    {
        _context = context;
        _controller = controller;
        _hideCompletion = hideCompletion;
        _resetHistoryNavigation = resetHistoryNavigation;
    }

    public override UiLayerInputPolicy InputPolicy => HasCommittedFrame && CommittedFrame.Visible ? UiLayerInputPolicy.Bubble : UiLayerInputPolicy.None;

    protected override CommandCompletionFrame RenderFrame(UiRenderContext context)
    {
        var completion = _context.CommandCompletion;
        var list = completion.List;
        var empty = new CommandCompletionFrame(false, context.Viewport, default, default, [], null, 0, list.ScrollTop, list.SelectedIndex, list.Count, ScrollableListFrameState.Empty);
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
        ScrollableListFrameState candidateState = list.CalculateFrameState(rowCount, candidateScrollbarBounds);
        ScrollState? scrollState = list.GetScrollState(rowCount, candidateState.ScrollTop);
        Rect? scrollbarBounds = scrollState is not null && ScrollBarInteraction.IsInteractive(candidateScrollbarBounds, scrollState)
            ? candidateScrollbarBounds
            : null;
        ScrollableListFrameState listState = list.CalculateFrameState(rowCount, scrollbarBounds);

        var popupOptions = PaletteStyles.DialogPopupOptions(_context.App.Palette) with
        {
            DrawShadow = false,
            VerticalScrollState = list.GetScrollState(rowCount, listState.ScrollTop),
        };
        _popupRenderer.RenderPopup(context.Screen, popupBounds, popupOptions, (screen, bounds) =>
            list.Render(screen, bounds, listState, PaletteStyles.DialogFill(_context.App.Palette), PaletteStyles.InputField(_context.App.Palette), PaletteStyles.DialogFill(_context.App.Palette)));

        var items = Enumerable.Range(0, rowCount).Select(row =>
        {
            int index = listState.ScrollTop + row;
            return new CommandCompletionItemFrame(index, list.Items[index], new Rect(contentBounds.X, contentBounds.Y + row, contentBounds.Width, 1), ItemTarget(index));
        }).ToArray();
        return new CommandCompletionFrame(true, context.Viewport, popupBounds, contentBounds, items, scrollbarBounds, rowCount, listState.ScrollTop, listState.SelectedIndex, list.Count, listState);
    }

    protected override void OnFrameCommitted(CommandCompletionFrame frame)
    {
        var list = _context.CommandCompletion.List;
        if (!frame.Visible)
        {
            list.ClearScrollbarDrag();
            return;
        }

        if (_context.CommandCompletion.Visible && list.Count == frame.MatchCount)
            list.ApplyCommittedFrame(frame.ListState);
    }

    protected override UiInteractionFrame BuildInteractionFrame(CommandCompletionFrame frame)
    {
        if (!frame.Visible)
            return UiInteractionFrame.Empty;
        var builder = new UiInteractionFrameBuilder().AddHitRegions(frame.Items.Select(item => new UiHitRegion(item.Target, item.Bounds)));
        if (frame.ScrollbarBounds is { } scrollbar)
            builder.AddHitRegion(ScrollbarTarget, scrollbar);
        return builder.Build();
    }

    protected override UiInputResult RouteInput(ConsoleInputEvent input, CommandCompletionFrame frame, UiInputRouteContext context) =>
        !frame.Visible || frame.VisibleRows <= 0 || frame.MatchCount == 0 ? UiInputResult.NotHandled : input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };

    private UiInputResult RouteKey(ConsoleKeyInfo key, CommandCompletionFrame frame)
    {
        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            return ToUiResult(_context.CommandCompletion.List.HandleKey(key, frame.VisibleRows));
        if (key.Key == ConsoleKey.Enter)
            return KeyboardShortcutClassifier.IsPlainControlEnter(key) ? UiInputResult.NotHandled : Accept(frame);
        if (key.Key == ConsoleKey.Escape)
        {
            _hideCompletion(true);
            return UiInputResult.HandledAndInvalidate;
        }
        if (key.Key == ConsoleKey.Delete && _controller.TryRemoveSelectedCommand(_context.CommandLine))
        {
            _resetHistoryNavigation();
            return UiInputResult.HandledAndInvalidate;
        }
        return UiInputResult.NotHandled;
    }

    private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, CommandCompletionFrame frame, UiInputRouteContext route)
    {
        bool relevant = route.IsCapturedRoute || route.Target == ScrollbarTarget || (route.Target is not null && TryItemIndex(route.Target, out _));
        if (!relevant)
            return UiInputResult.NotHandled;

        ScrollableListInputResult result = _context.CommandCompletion.List.HandleMouse(mouse, frame.ContentBounds, frame.ScrollbarBounds, frame.VisibleRows, confirmOnMouseDown: true, confirmOnDoubleClick: true);
        if (!result.IsHandled)
            return UiInputResult.NotHandled;
        if (result.Kind == ScrollableListInputResultKind.Confirmed && route.Target is { } target && TryItemIndex(target, out int itemIndex))
            return AcceptItem(itemIndex, frame);
        if (result.DragStarted)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true);
        if (result.DragEnded)
            return UiInputResult.ReleaseMouse(invalidate: true);
        return UiInputResult.HandledAndInvalidate;
    }

    private UiInputResult Accept(CommandCompletionFrame frame) => AcceptItem(frame.SelectedIndex, frame);

    private UiInputResult AcceptItem(int itemIndex, CommandCompletionFrame frame)
    {
        var completion = _context.CommandCompletion;
        if (!TryGetCommittedItem(itemIndex, frame, out var item) || itemIndex >= completion.List.Count || !string.Equals(completion.List.Items[itemIndex], item.Text, StringComparison.Ordinal))
            return UiInputResult.NotHandled;
        if (itemIndex == 0)
        {
            _hideCompletion(false);
            _resetHistoryNavigation();
            return UiInputResult.NotHandled;
        }
        _context.CommandLine.SetText(item.Text);
        _hideCompletion(false);
        _resetHistoryNavigation();
        return UiInputResult.HandledAndInvalidate;
    }

    private static UiInputResult ToUiResult(ScrollableListInputResult result) => result.IsHandled ? UiInputResult.HandledAndInvalidate : UiInputResult.NotHandled;
    private static bool TryGetCommittedItem(int index, CommandCompletionFrame frame, out CommandCompletionItemFrame item)
    {
        item = frame.Items.FirstOrDefault(candidate => candidate.AbsoluteIndex == index)!;
        return item is not null;
    }
    private static UiTargetId ItemTarget(int index) => new($"application.command-completion.item:{index}");
    private static bool TryItemIndex(UiTargetId target, out int index)
    {
        const string prefix = "application.command-completion.item:";
        if (target.Value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(target.Value[prefix.Length..], out index))
            return true;

        index = -1;
        return false;
    }
}
