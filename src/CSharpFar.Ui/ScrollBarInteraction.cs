using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum ScrollBarHitPart
{
    None,
    DecreaseButton,
    IncreaseButton,
    TrackBeforeThumb,
    TrackAfterThumb,
    Thumb,
}

public readonly record struct ScrollBarHitTestResult(
    ScrollBarHitPart Part,
    int PointerOffsetInThumb);

public readonly record struct ScrollBarDragState(
    Rect Bounds,
    int TotalItems,
    int ViewportItems,
    int PointerOffsetInThumb);

public readonly record struct ScrollBarThumb(
    int TrackHeight,
    int ThumbHeight,
    int ThumbY);

public static class ScrollBarInteraction
{
    public static ScrollBarDragState? RebaseDrag(
        ScrollBarDragState drag,
        Rect bounds,
        int totalItems,
        int viewportItems)
    {
        var state = new ScrollState
        {
            TotalItems = totalItems,
            ViewportItems = viewportItems,
            FirstVisibleIndex = 0,
        };
        if (!IsInteractive(bounds, state))
            return null;

        var thumb = CalculateThumb(bounds, state);
        return new ScrollBarDragState(
            bounds,
            totalItems,
            viewportItems,
            Math.Clamp(drag.PointerOffsetInThumb, 0, thumb.ThumbHeight - 1));
    }

    public static ScrollBarHitTestResult HitTest(Rect bounds, ScrollState state, int x, int y)
    {
        if (!IsInteractive(bounds, state) || x != bounds.X || y < bounds.Y || y >= bounds.Bottom)
            return new ScrollBarHitTestResult(ScrollBarHitPart.None, 0);

        if (y == bounds.Y)
            return new ScrollBarHitTestResult(ScrollBarHitPart.DecreaseButton, 0);

        if (y == bounds.Bottom - 1)
            return new ScrollBarHitTestResult(ScrollBarHitPart.IncreaseButton, 0);

        var thumb = CalculateThumb(bounds, state);
        if (y < thumb.ThumbY)
            return new ScrollBarHitTestResult(ScrollBarHitPart.TrackBeforeThumb, 0);

        if (y >= thumb.ThumbY + thumb.ThumbHeight)
            return new ScrollBarHitTestResult(ScrollBarHitPart.TrackAfterThumb, 0);

        return new ScrollBarHitTestResult(ScrollBarHitPart.Thumb, y - thumb.ThumbY);
    }

    public static int ApplyClick(ScrollState state, ScrollBarHitPart part)
    {
        int firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            state.FirstVisibleIndex,
            state.TotalItems,
            state.ViewportItems);

        int delta = part switch
        {
            ScrollBarHitPart.DecreaseButton => -1,
            ScrollBarHitPart.IncreaseButton => +1,
            ScrollBarHitPart.TrackBeforeThumb => -Math.Max(1, state.ViewportItems),
            ScrollBarHitPart.TrackAfterThumb => +Math.Max(1, state.ViewportItems),
            _ => 0,
        };

        return ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex + delta,
            state.TotalItems,
            state.ViewportItems);
    }

    public static int FirstVisibleIndexForThumbY(
        Rect bounds,
        ScrollState state,
        int pointerY,
        int pointerOffsetInThumb)
    {
        if (!IsInteractive(bounds, state))
            return 0;

        var thumb = CalculateThumb(bounds, state);
        int maxFirstVisibleIndex = Math.Max(0, state.TotalItems - state.ViewportItems);
        int movableTrackHeight = Math.Max(0, thumb.TrackHeight - thumb.ThumbHeight);
        if (movableTrackHeight == 0 || maxFirstVisibleIndex == 0)
            return 0;

        int trackY = bounds.Y + 1;
        int thumbOffset = Math.Clamp(
            pointerY - pointerOffsetInThumb - trackY,
            0,
            movableTrackHeight);

        int firstVisibleIndex = (int)Math.Round(
            (double)thumbOffset * maxFirstVisibleIndex / movableTrackHeight);

        return ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            state.TotalItems,
            state.ViewportItems);
    }

    public static ScrollBarThumb CalculateThumb(Rect bounds, ScrollState state)
    {
        int trackHeight = Math.Max(0, bounds.Height - 2);
        if (trackHeight == 0)
            return new ScrollBarThumb(0, 0, bounds.Y + 1);

        int viewportItems = Math.Max(1, state.ViewportItems);
        int totalItems = Math.Max(viewportItems, state.TotalItems);
        int thumbHeight = Math.Clamp(
            (int)Math.Round((double)viewportItems * trackHeight / totalItems),
            1,
            trackHeight);

        int maxFirstVisibleIndex = Math.Max(0, totalItems - viewportItems);
        int movableTrackHeight = Math.Max(0, trackHeight - thumbHeight);
        int firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            state.FirstVisibleIndex,
            totalItems,
            viewportItems);
        int thumbOffset = maxFirstVisibleIndex == 0 || movableTrackHeight == 0
            ? 0
            : (int)Math.Round((double)firstVisibleIndex * movableTrackHeight / maxFirstVisibleIndex);

        return new ScrollBarThumb(trackHeight, thumbHeight, bounds.Y + 1 + thumbOffset);
    }

    public static bool IsInteractive(Rect bounds, ScrollState state) =>
        bounds.Height >= 3 &&
        state.TotalItems > state.ViewportItems &&
        state.ViewportItems > 0;
}
