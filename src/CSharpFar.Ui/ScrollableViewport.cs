using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum ScrollableViewportInputResultKind
{
    NotHandled,
    Handled,
    PositionChanged,
}

public readonly record struct ScrollableViewportInputResult(
    ScrollableViewportInputResultKind Kind,
    bool DragStarted = false,
    bool DragEnded = false)
{
    public static ScrollableViewportInputResult NotHandled => new(ScrollableViewportInputResultKind.NotHandled);

    public bool IsHandled => Kind != ScrollableViewportInputResultKind.NotHandled;

    public bool PositionChanged => Kind == ScrollableViewportInputResultKind.PositionChanged;
}

public readonly record struct ScrollableViewportFrameState(
    int TotalItems,
    int ViewportItems,
    int FirstVisibleIndex,
    Rect ContentBounds,
    VerticalScrollbarFrame? ScrollbarFrame = null)
{
    public Rect? ScrollbarBounds => ScrollbarFrame?.Bounds;
    public ScrollBarDragState? ScrollbarDrag => ScrollbarFrame?.DragState;
}

/// <summary>Owns standard vertical scrolling for content without selection.</summary>
public sealed class ScrollableViewport
{
    private readonly VerticalScrollbarController _scrollbar = new();

    public int FirstVisibleIndex { get; set; }

    public ScrollBarDragState? ScrollbarDrag => _scrollbar.DragState;

    public ScrollableViewportFrameState CalculateFrameState(
        int totalItems,
        int viewportItems,
        Rect contentBounds,
        Rect? scrollbarBounds)
    {
        int items = Math.Max(0, totalItems);
        int viewport = Math.Max(1, viewportItems);
        int first = ScrollStateCalculator.ClampFirstVisibleIndex(FirstVisibleIndex, items, viewport);
        var state = new ScrollState
        {
            TotalItems = items,
            ViewportItems = viewport,
            FirstVisibleIndex = first,
        };
        return new ScrollableViewportFrameState(
            items,
            viewport,
            first,
            contentBounds,
            _scrollbar.CalculateFrame(scrollbarBounds, state));
    }

    public void ApplyCommittedFrame(ScrollableViewportFrameState frame)
    {
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            frame.FirstVisibleIndex,
            Math.Max(0, frame.TotalItems),
            Math.Max(1, frame.ViewportItems));
        _scrollbar.ApplyCommittedFrame(frame.ScrollbarFrame);
    }

    public ScrollState? GetScrollState(ScrollableViewportFrameState frame) =>
        frame.TotalItems > frame.ViewportItems
            ? new ScrollState
            {
                TotalItems = frame.TotalItems,
                ViewportItems = frame.ViewportItems,
                FirstVisibleIndex = frame.FirstVisibleIndex,
            }
            : null;

    public ScrollableViewportInputResult HandleKey(ConsoleKeyInfo key, ScrollableViewportFrameState frame)
    {
        int target = key.Key switch
        {
            ConsoleKey.UpArrow => frame.FirstVisibleIndex - 1,
            ConsoleKey.DownArrow => frame.FirstVisibleIndex + 1,
            ConsoleKey.PageUp => frame.FirstVisibleIndex - frame.ViewportItems,
            ConsoleKey.PageDown => frame.FirstVisibleIndex + frame.ViewportItems,
            ConsoleKey.Home => 0,
            ConsoleKey.End => frame.TotalItems - frame.ViewportItems,
            _ => int.MinValue,
        };
        if (target == int.MinValue)
            return ScrollableViewportInputResult.NotHandled;

        return SetFirstVisibleIndex(target, frame);
    }

    public ScrollableViewportInputResult HandleMouse(
        MouseConsoleInputEvent mouse,
        ScrollableViewportFrameState frame,
        int wheelStep = 3)
    {
        if (mouse.Kind == MouseEventKind.Wheel)
        {
            bool insideContent = frame.ContentBounds.Contains(mouse.X, mouse.Y);
            bool insideScrollbar = frame.ScrollbarBounds is Rect scrollbar && scrollbar.Contains(mouse.X, mouse.Y);
            if (!insideContent && !insideScrollbar)
                return ScrollableViewportInputResult.NotHandled;

            int step = Math.Max(1, wheelStep);
            return mouse.Button switch
            {
                MouseButton.WheelUp => SetFirstVisibleIndex(frame.FirstVisibleIndex - step, frame),
                MouseButton.WheelDown => SetFirstVisibleIndex(frame.FirstVisibleIndex + step, frame),
                _ => ScrollableViewportInputResult.NotHandled,
            };
        }

        if (frame.ScrollbarFrame is not { } scrollbarFrame)
            return ScrollableViewportInputResult.NotHandled;

        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(mouse, scrollbarFrame);
        if (!result.IsHandled)
            return ScrollableViewportInputResult.NotHandled;

        FirstVisibleIndex = result.FirstVisibleIndex;
        return new ScrollableViewportInputResult(
            result.PositionChanged
                ? ScrollableViewportInputResultKind.PositionChanged
                : ScrollableViewportInputResultKind.Handled,
            result.DragStarted,
            result.DragEnded);
    }

    private ScrollableViewportInputResult SetFirstVisibleIndex(int requested, ScrollableViewportFrameState frame)
    {
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            requested, frame.TotalItems, frame.ViewportItems);
        return new ScrollableViewportInputResult(
            FirstVisibleIndex == frame.FirstVisibleIndex
                ? ScrollableViewportInputResultKind.Handled
                : ScrollableViewportInputResultKind.PositionChanged);
    }
}
