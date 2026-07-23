using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public readonly record struct VerticalScrollbarFrame(
    Rect Bounds,
    int TotalItems,
    int ViewportItems,
    int FirstVisibleIndex,
    ScrollBarDragState? DragState = null)
{
    public ScrollState ToScrollState() => new()
    {
        TotalItems = TotalItems,
        ViewportItems = ViewportItems,
        FirstVisibleIndex = FirstVisibleIndex,
    };
}

public readonly record struct VerticalScrollbarInputResult(
    bool IsHandled,
    int FirstVisibleIndex,
    bool PositionChanged,
    bool DragStarted = false,
    bool DragEnded = false)
{
    public static VerticalScrollbarInputResult NotHandled(int firstVisibleIndex = 0) =>
        new(IsHandled: false, firstVisibleIndex, PositionChanged: false);
}

public sealed class VerticalScrollbarController
{
    private ScrollBarDragState? _dragState;

    public ScrollBarDragState? DragState => _dragState;

    public VerticalScrollbarFrame? CalculateFrame(Rect? bounds, ScrollState? state)
    {
        if (bounds is not { } effectiveBounds ||
            state is null ||
            !ScrollBarInteraction.IsInteractive(effectiveBounds, state))
        {
            return null;
        }

        int totalItems = Math.Max(0, state.TotalItems);
        int viewportItems = Math.Max(1, state.ViewportItems);
        int firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            state.FirstVisibleIndex,
            totalItems,
            viewportItems);
        ScrollBarDragState? rebasedDrag = _dragState is { } drag
            ? ScrollBarInteraction.RebaseDrag(drag, effectiveBounds, totalItems, viewportItems)
            : null;

        return new VerticalScrollbarFrame(
            effectiveBounds,
            totalItems,
            viewportItems,
            firstVisibleIndex,
            rebasedDrag);
    }

    public void ApplyCommittedFrame(VerticalScrollbarFrame? frame)
    {
        _dragState = frame?.DragState;
    }

    public void ClearDrag() => _dragState = null;

    public VerticalScrollbarInputResult HandleMouse(
        MouseConsoleInputEvent input,
        VerticalScrollbarFrame frame)
    {
        int currentFirst = ScrollStateCalculator.ClampFirstVisibleIndex(
            frame.FirstVisibleIndex,
            frame.TotalItems,
            frame.ViewportItems);
        ScrollBarDragState? drag = _dragState ?? frame.DragState;
        bool wasDragging = drag.HasValue;

        if (input.Kind == MouseEventKind.Up)
        {
            if (input.Button != MouseButton.Left)
                return VerticalScrollbarInputResult.NotHandled(currentFirst);
            if (!wasDragging)
                return VerticalScrollbarInputResult.NotHandled(currentFirst);

            _dragState = null;
            return new VerticalScrollbarInputResult(
                IsHandled: true,
                currentFirst,
                PositionChanged: false,
                DragEnded: true);
        }

        if (drag is { } activeDrag && input.Kind == MouseEventKind.Move)
        {
            int firstVisibleIndex = ScrollBarInteraction.FirstVisibleIndexForThumbY(
                activeDrag.Bounds,
                new ScrollState
                {
                    TotalItems = activeDrag.TotalItems,
                    ViewportItems = activeDrag.ViewportItems,
                    FirstVisibleIndex = currentFirst,
                },
                input.Y,
                activeDrag.PointerOffsetInThumb);
            firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
                firstVisibleIndex,
                activeDrag.TotalItems,
                activeDrag.ViewportItems);
            _dragState = activeDrag;
            return new VerticalScrollbarInputResult(
                IsHandled: true,
                firstVisibleIndex,
                firstVisibleIndex != currentFirst);
        }

        if (input is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            return VerticalScrollbarInputResult.NotHandled(currentFirst);

        ScrollState state = frame.ToScrollState();
        ScrollBarHitTestResult hit = ScrollBarInteraction.HitTest(frame.Bounds, state, input.X, input.Y);
        if (hit.Part == ScrollBarHitPart.None)
            return VerticalScrollbarInputResult.NotHandled(currentFirst);

        if (hit.Part == ScrollBarHitPart.Thumb)
        {
            _dragState = new ScrollBarDragState(
                frame.Bounds,
                frame.TotalItems,
                frame.ViewportItems,
                hit.PointerOffsetInThumb);
            return new VerticalScrollbarInputResult(
                IsHandled: true,
                currentFirst,
                PositionChanged: false,
                DragStarted: true);
        }

        _dragState = null;
        int clickedFirst = ScrollBarInteraction.ApplyClick(state, hit.Part);
        return new VerticalScrollbarInputResult(
            IsHandled: true,
            clickedFirst,
            clickedFirst != currentFirst);
    }
}
