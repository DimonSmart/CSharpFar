using CSharpFar.App.CommandLine;
using CSharpFar.App.Input;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.App.State;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record CommandCompletionItemFrame(
    int AbsoluteIndex,
    string Text,
    Rect Bounds,
    UiTargetId Target);

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
    int MatchCount);

internal sealed class CommandCompletionLayer : UiLayer<CommandCompletionFrame>
{
    private const int MaxVisibleRows = CommandHistoryCompletionRenderer.MaxVisibleRows;
    private static readonly UiTargetId ScrollbarTarget = new("application.command-completion.scrollbar");

    private readonly ApplicationRenderContext _context;
    private readonly CommandCompletionController _controller;
    private readonly Action<bool> _hideCompletion;
    private readonly Action _resetHistoryNavigation;

    public CommandCompletionLayer(
        ApplicationRenderContext context,
        CommandCompletionController controller,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
    {
        _context = context;
        _controller = controller;
        _hideCompletion = hideCompletion;
        _resetHistoryNavigation = resetHistoryNavigation;
    }

    public override UiLayerInputPolicy InputPolicy =>
        HasCommittedFrame && CommittedFrame.Visible
            ? UiLayerInputPolicy.Bubble
            : UiLayerInputPolicy.None;

    protected override CommandCompletionFrame RenderFrame(UiRenderContext context)
    {
        var completion = _context.CommandCompletion;
        var empty = new CommandCompletionFrame(
            false,
            context.Viewport,
            default,
            default,
            [],
            null,
            0,
            completion.FirstVisibleIndex,
            completion.SelectedIndex,
            completion.Matches.Count);

        if (_context.App.WorkspaceMode != ApplicationWorkspaceMode.Panels)
            return empty;

        int visibleRows = VisibleRows(context.Size);
        if (!completion.Visible || completion.Matches.Count == 0 || visibleRows <= 0)
            return empty;

        int rowCount = Math.Min(visibleRows, completion.Matches.Count);
        int selected = Math.Clamp(completion.SelectedIndex, 0, completion.Matches.Count - 1);
        int first = ScrollStateCalculator.EnsureIndexVisible(selected, completion.FirstVisibleIndex, rowCount);
        first = ScrollStateCalculator.ClampFirstVisibleIndex(first, completion.Matches.Count, rowCount);
        int height = rowCount + 2;
        int commandLineRow = ApplicationLayoutService.CommandLineRow(context.Size);
        var popupBounds = new Rect(0, commandLineRow - height, context.Size.Width, height);
        var contentBounds = new Rect(1, popupBounds.Y + 1, Math.Max(0, popupBounds.Width - 2), rowCount);
        Rect candidateScrollbarBounds = new(popupBounds.Right - 1, popupBounds.Y + 1, 1, rowCount);
        var scrollbarState = new ScrollState
        {
            TotalItems = completion.Matches.Count,
            ViewportItems = rowCount,
            FirstVisibleIndex = first,
        };
        Rect? scrollbarBounds = completion.Matches.Count > rowCount &&
            ScrollBarInteraction.IsInteractive(candidateScrollbarBounds, scrollbarState)
                ? candidateScrollbarBounds
                : null;

        new CommandHistoryCompletionRenderer(context.Screen, _context.App.Palette).Render(
            commandLineRow,
            context.Size.Width,
            completion.Matches,
            selected,
            first);

        var items = Enumerable.Range(0, rowCount)
            .Select(row =>
            {
                int absoluteIndex = first + row;
                return new CommandCompletionItemFrame(
                    absoluteIndex,
                    completion.Matches[absoluteIndex],
                    new Rect(contentBounds.X, contentBounds.Y + row, contentBounds.Width, 1),
                    ItemTarget(absoluteIndex));
            })
            .ToArray();

        return new CommandCompletionFrame(
            true,
            context.Viewport,
            popupBounds,
            contentBounds,
            items,
            scrollbarBounds,
            rowCount,
            first,
            selected,
            completion.Matches.Count);
    }

    protected override void OnFrameCommitted(CommandCompletionFrame frame)
    {
        if (!frame.Visible || frame.ScrollbarBounds is null)
        {
            _context.CommandCompletion.ScrollbarDrag = null;
        }
        else if (_context.CommandCompletion.ScrollbarDrag is { } drag)
        {
            _context.CommandCompletion.ScrollbarDrag = ScrollBarInteraction.RebaseDrag(
                drag,
                frame.ScrollbarBounds.Value,
                frame.MatchCount,
                frame.VisibleRows);
        }

        if (!frame.Visible)
            return;

        var completion = _context.CommandCompletion;
        if (!completion.Visible || completion.Matches.Count != frame.MatchCount)
            return;

        completion.SelectedIndex = Math.Clamp(frame.SelectedIndex, 0, completion.Matches.Count - 1);
        completion.FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            frame.FirstVisibleIndex,
            completion.Matches.Count,
            frame.VisibleRows);
    }

    protected override UiInteractionFrame BuildInteractionFrame(CommandCompletionFrame frame)
    {
        if (!frame.Visible)
            return UiInteractionFrame.Empty;

        var hitRegions = frame.Items
            .Select(item => new UiHitRegion(item.Target, item.Bounds))
            .ToList();
        if (frame.ScrollbarBounds is { } scrollbar)
            hitRegions.Add(new UiHitRegion(ScrollbarTarget, scrollbar));

        return new UiInteractionFrame(hitRegions);
    }

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        CommandCompletionFrame frame,
        UiInputRouteContext context)
    {
        if (!frame.Visible || frame.VisibleRows <= 0 || frame.MatchCount == 0)
            return UiInputResult.NotHandled;

        return input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };
    }

    private UiInputResult RouteKey(ConsoleKeyInfo key, CommandCompletionFrame frame)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                return Move(-1, frame);
            case ConsoleKey.DownArrow:
                return Move(+1, frame);
            case ConsoleKey.Enter:
                if (KeyboardShortcutClassifier.IsPlainControlEnter(key))
                    return UiInputResult.NotHandled;
                return Accept(frame);
            case ConsoleKey.Escape:
                _hideCompletion(true);
                return UiInputResult.HandledAndInvalidate;
            case ConsoleKey.Delete:
                if (!_controller.TryRemoveSelectedCommand(_context.CommandLine, frame.VisibleRows))
                    return UiInputResult.NotHandled;
                _resetHistoryNavigation();
                return UiInputResult.HandledAndInvalidate;
            default:
                return UiInputResult.NotHandled;
        }
    }

    private UiInputResult Move(int delta, CommandCompletionFrame frame) =>
        _controller.TryMoveSelection(delta, frame.VisibleRows)
            ? UiInputResult.HandledAndInvalidate
            : UiInputResult.NotHandled;

    private UiInputResult Accept(CommandCompletionFrame frame)
    {
        var completion = _context.CommandCompletion;
        if (!completion.Visible || completion.Matches.Count == 0 || frame.VisibleRows <= 0)
            return UiInputResult.NotHandled;

        if (frame.SelectedIndex == 0)
        {
            _hideCompletion(false);
            _resetHistoryNavigation();
            return UiInputResult.NotHandled;
        }

        if (!TryGetCommittedItem(frame.SelectedIndex, frame, out var item) ||
            item.AbsoluteIndex >= completion.Matches.Count ||
            !string.Equals(completion.Matches[item.AbsoluteIndex], item.Text, StringComparison.Ordinal))
        {
            return UiInputResult.NotHandled;
        }

        _context.CommandLine.SetText(item.Text);
        _hideCompletion(false);
        _resetHistoryNavigation();
        return UiInputResult.HandledAndInvalidate;
    }

    private UiInputResult RouteMouse(
        MouseConsoleInputEvent mouse,
        CommandCompletionFrame frame,
        UiInputRouteContext route)
    {
        if (route.Target == ScrollbarTarget || route.IsCapturedRoute)
            return RouteScrollbarMouse(mouse, frame, route.IsCapturedRoute);

        if (route.Target is not null &&
            mouse.Button == MouseButton.Left &&
            mouse.Kind is MouseEventKind.Down or MouseEventKind.DoubleClick &&
            TryItemIndex(route.Target, out int itemIndex))
        {
            return AcceptItem(itemIndex, frame);
        }

        return UiInputResult.NotHandled;
    }

    private UiInputResult AcceptItem(int itemIndex, CommandCompletionFrame frame)
    {
        var completion = _context.CommandCompletion;
        if (!TryGetCommittedItem(itemIndex, frame, out var item) ||
            itemIndex < 0 ||
            itemIndex >= completion.Matches.Count ||
            !string.Equals(completion.Matches[itemIndex], item.Text, StringComparison.Ordinal))
        {
            return UiInputResult.NotHandled;
        }

        completion.SelectedIndex = itemIndex;
        if (itemIndex == 0)
        {
            _hideCompletion(false);
            return UiInputResult.HandledAndInvalidate;
        }

        _context.CommandLine.SetText(item.Text);
        _hideCompletion(false);
        _resetHistoryNavigation();
        return UiInputResult.HandledAndInvalidate;
    }

    private static bool TryGetCommittedItem(
        int absoluteIndex,
        CommandCompletionFrame frame,
        out CommandCompletionItemFrame item)
    {
        foreach (var candidate in frame.Items)
        {
            if (candidate.AbsoluteIndex == absoluteIndex)
            {
                item = candidate;
                return true;
            }
        }

        item = null!;
        return false;
    }

    private UiInputResult RouteScrollbarMouse(
        MouseConsoleInputEvent mouse,
        CommandCompletionFrame frame,
        bool captured)
    {
        if (frame.ScrollbarBounds is not { } scrollbar)
            return captured ? UiInputResult.ReleaseMouse(invalidate: true) : UiInputResult.NotHandled;

        int firstVisibleIndex = _context.CommandCompletion.FirstVisibleIndex;
        ScrollBarDragState? dragState = _context.CommandCompletion.ScrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                scrollbar,
                frame.MatchCount,
                frame.VisibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return UiInputResult.NotHandled;
        }

        _context.CommandCompletion.ScrollbarDrag = dragState;
        _context.CommandCompletion.FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            frame.MatchCount,
            frame.VisibleRows);
        _controller.ClampSelectionToViewport(frame.VisibleRows);

        if (mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true);

        if (captured && mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Up)
            return UiInputResult.ReleaseMouse(invalidate: true);

        return UiInputResult.HandledAndInvalidate;
    }

    private static int VisibleRows(ConsoleSize size)
    {
        int rowsAboveCommandLine = ApplicationLayoutService.CommandLineRow(size) - 2;
        return Math.Max(0, Math.Min(MaxVisibleRows, rowsAboveCommandLine));
    }

    private static UiTargetId ItemTarget(int absoluteIndex) =>
        new($"application.command-completion.item:{absoluteIndex}");

    private static bool TryItemIndex(UiTargetId target, out int index)
    {
        const string prefix = "application.command-completion.item:";
        if (target.Value.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(target.Value[prefix.Length..], out index))
        {
            return true;
        }

        index = -1;
        return false;
    }
}
