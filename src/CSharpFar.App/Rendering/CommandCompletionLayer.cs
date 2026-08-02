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
    private readonly ScrollableListRenderOptions<string> _presentation;
    internal ScrollBarDragState? ScrollbarDragState => _list.ScrollbarDragState;

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
        _list = new RoutedScrollableList<string>(
            context.CommandCompletion.List,
            ListTarget,
            ScrollbarTarget,
            new RoutedScrollableListInteractionOptions
            {
                AcceptKeyboardFromLayerRoute = true,
            });
        _presentation = new(static item => item, string.Empty, PaletteStyles.DialogFill(_context.App.Palette), PaletteStyles.InputField(_context.App.Palette), PaletteStyles.DialogFill(_context.App.Palette));
    }

    public override UiLayerInputPolicy InputPolicy =>
        HasCommittedFrame &&
        CommittedFrame.Visible &&
        _context.CommandCompletion.Visible &&
        (_context.App.WorkspaceMode != ApplicationWorkspaceMode.HiddenCommandLine || !_context.Ui.HiddenUiDetachedByScroll)
            ? UiLayerInputPolicy.Bubble
            : UiLayerInputPolicy.None;

    protected override CommandCompletionFrame RenderFrame(UiRenderContext context)
    {
        var completion = _context.CommandCompletion;
        var list = _list;
        var empty = new CommandCompletionFrame(false, context.Viewport, default, default, [], null, 0, completion.List.Count, new RoutedScrollableListFrame(ScrollableListFrame.Empty(default)));
        if (!completion.Visible || !completion.List.HasItems)
            return empty;

        CommandCompletionLayoutFrame layout = CommandCompletionLayout.Calculate(context.Size, completion.List.Count);
        if (!layout.IsVisible)
            return empty;

        RoutedScrollableListFrame candidate = list.CalculateFrame(layout.ContentBounds, layout.CandidateScrollbarBounds);
        ScrollState? scrollState = completion.List.Count > layout.VisibleRows ? new ScrollState { TotalItems = completion.List.Count, ViewportItems = layout.VisibleRows, FirstVisibleIndex = candidate.List.ScrollTop } : null;
        Rect? scrollbarBounds = scrollState is not null && ScrollBarInteraction.IsInteractive(layout.CandidateScrollbarBounds, scrollState)
            ? layout.CandidateScrollbarBounds
            : null;
        RoutedScrollableListFrame listFrame = list.CalculateFrame(layout.ContentBounds, scrollbarBounds);

        var popupOptions = PaletteStyles.DialogPopupOptions(_context.App.Palette) with
        {
            DrawShadow = false,
            VerticalScrollState = completion.List.Count > layout.VisibleRows ? new ScrollState { TotalItems = completion.List.Count, ViewportItems = layout.VisibleRows, FirstVisibleIndex = listFrame.List.ScrollTop } : null,
        };
        _popupRenderer.RenderPopup(context.Canvas, layout.PopupBounds, popupOptions, (screen, bounds) =>
            list.Render(screen, listFrame, _presentation));

        var items = Enumerable.Range(0, layout.VisibleRows).Select(row =>
        {
            int index = listFrame.List.ScrollTop + row;
            return new CommandCompletionItemFrame(index, completion.List.Items[index], new Rect(layout.ContentBounds.X, layout.ContentBounds.Y + row, layout.ContentBounds.Width, 1));
        }).ToArray();
        return new CommandCompletionFrame(true, context.Viewport, layout.PopupBounds, layout.ContentBounds, items, scrollbarBounds, layout.VisibleRows, completion.List.Count, listFrame);
    }

    protected override UiInteractionFrame BuildInteractionFrame(CommandCompletionFrame frame)
    {
        if (!frame.Visible)
            return UiInteractionFrame.Empty;
        return new UiInteractionFrameBuilder()
            .AddFragment(_list.BuildInteractionFragment(frame.List, tabOrder: 0))
            .Build();
    }

    protected override void OnFrameCommitted(CommandCompletionFrame frame) =>
        _list.SynchronizeCommittedScrollbar(frame.List);

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
            return InvalidateCompletion();

        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            return InvalidateCompletion(_list.RouteInput(new KeyConsoleInputEvent(key), frame.List, route).UiResult);
        if (key.Key == ConsoleKey.Enter)
            return KeyboardShortcutClassifier.IsPlainControlEnter(key) ? UiInputResult.NotHandled : AcceptByKeyboard(frame);
        if (key.Key == ConsoleKey.Escape)
        {
            _hideCompletion(true);
            return InvalidateCompletion();
        }
        if (key.Key == ConsoleKey.Delete && _controller.TryRemoveSelectedCommand(_context.CommandLine, frame.List.List.SelectedIndex, frame.VisibleRows))
        {
            _resetHistoryNavigation();
            return InvalidateCompletion();
        }
        return UiInputResult.NotHandled;
    }

    private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, CommandCompletionFrame frame, UiInputRouteContext route)
    {
        if (!TryRestoreCommittedList(frame))
            return InvalidateCompletion();

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
            return AcceptByMouse(_list.State.SelectedIndex, frame);
        return InvalidateCompletion(routed.UiResult);
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
            return continueRoutingForNeutralItem ? UiInputResult.NotHandled : InvalidateCompletion();
        }
        _context.CommandLine.SetText(item.Text);
        _hideCompletion(false);
        _resetHistoryNavigation();
        return InvalidateCommandLineAndCompletion();
    }

    private UiInputResult InvalidateCompletion() =>
        InvalidateCompletion(UiInputResult.HandledAndInvalidate);

    private UiInputResult InvalidateCompletion(UiInputResult result)
    {
        return result;
    }

    private UiInputResult InvalidateCommandLineAndCompletion()
    {
        return UiInputResult.HandledAndInvalidate;
    }

    private bool TryRestoreCommittedList(CommandCompletionFrame frame)
    {
        var completion = _context.CommandCompletion;
        if (!completion.Visible ||
            completion.List.Count != frame.MatchCount ||
            frame.Items.Any(item => item.AbsoluteIndex >= completion.List.Count || !string.Equals(completion.List.Items[item.AbsoluteIndex], item.Text, StringComparison.Ordinal)))
            return false;

        return true;
    }

    private static bool TryGetCommittedItem(int index, CommandCompletionFrame frame, out CommandCompletionItemFrame item)
    {
        item = frame.Items.FirstOrDefault(candidate => candidate.AbsoluteIndex == index)!;
        return item is not null;
    }
}
