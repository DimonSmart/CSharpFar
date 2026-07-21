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
    Rect? ScrollbarBounds = null,
    ScrollBarDragState? ScrollbarDrag = null);

/// <summary>Owns standard vertical scrolling for content without selection.</summary>
public sealed class ScrollableViewport
{
    private ScrollBarDragState? _scrollbarDrag;

    public int FirstVisibleIndex { get; set; }

    public ScrollBarDragState? ScrollbarDrag => _scrollbarDrag;

    public ScrollableViewportFrameState CalculateFrameState(
        int totalItems,
        int viewportItems,
        Rect contentBounds,
        Rect? scrollbarBounds)
    {
        int items = Math.Max(0, totalItems);
        int viewport = Math.Max(1, viewportItems);
        int first = ScrollStateCalculator.ClampFirstVisibleIndex(FirstVisibleIndex, items, viewport);
        bool scrollable = items > viewport;
        Rect? effectiveScrollbar = scrollable ? scrollbarBounds : null;
        ScrollBarDragState? drag = _scrollbarDrag is { } current && effectiveScrollbar is { } bounds
            ? ScrollBarInteraction.RebaseDrag(current, bounds, items, viewport)
            : null;
        return new ScrollableViewportFrameState(items, viewport, first, contentBounds, effectiveScrollbar, drag);
    }

    public void ApplyCommittedFrame(ScrollableViewportFrameState frame)
    {
        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            frame.FirstVisibleIndex,
            Math.Max(0, frame.TotalItems),
            Math.Max(1, frame.ViewportItems));
        _scrollbarDrag = frame.ScrollbarDrag;
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

        if (frame.ScrollbarBounds is not Rect bounds)
            return ScrollableViewportInputResult.NotHandled;

        int first = frame.FirstVisibleIndex;
        ScrollBarDragState? drag = frame.ScrollbarDrag;
        bool wasDragging = drag.HasValue;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse, bounds, frame.TotalItems, frame.ViewportItems, ref first, ref drag))
        {
            return ScrollableViewportInputResult.NotHandled;
        }

        FirstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(first, frame.TotalItems, frame.ViewportItems);
        _scrollbarDrag = drag;
        bool isDragging = drag.HasValue;
        return new ScrollableViewportInputResult(
            FirstVisibleIndex != frame.FirstVisibleIndex
                ? ScrollableViewportInputResultKind.PositionChanged
                : ScrollableViewportInputResultKind.Handled,
            DragStarted: !wasDragging && isDragging,
            DragEnded: wasDragging && !isDragging);
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
