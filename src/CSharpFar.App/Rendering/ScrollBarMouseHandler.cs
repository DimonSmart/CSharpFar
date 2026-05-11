using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

public static class ScrollBarMouseHandler
{
    public static bool TryHandleMouse(
        MouseConsoleInputEvent mouse,
        Rect bounds,
        int totalItems,
        int viewportItems,
        ref int firstVisibleIndex,
        ref ScrollBarDragState? dragState)
    {
        if (mouse.Kind == MouseEventKind.Up)
        {
            bool wasDragging = dragState.HasValue;
            dragState = null;
            return wasDragging;
        }

        if (dragState is { } drag && mouse.Kind == MouseEventKind.Move)
        {
            firstVisibleIndex = ScrollBarInteraction.FirstVisibleIndexForThumbY(
                drag.Bounds,
                new ScrollState
                {
                    TotalItems = drag.TotalItems,
                    ViewportItems = drag.ViewportItems,
                    FirstVisibleIndex = firstVisibleIndex,
                },
                mouse.Y,
                drag.PointerOffsetInThumb);
            return true;
        }

        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        var state = new ScrollState
        {
            TotalItems = totalItems,
            ViewportItems = viewportItems,
            FirstVisibleIndex = firstVisibleIndex,
        };

        var hit = ScrollBarInteraction.HitTest(bounds, state, mouse.X, mouse.Y);
        if (hit.Part == ScrollBarHitPart.None)
            return false;

        if (hit.Part == ScrollBarHitPart.Thumb)
        {
            if (mouse.Kind == MouseEventKind.Down)
            {
                dragState = new ScrollBarDragState(
                    bounds,
                    totalItems,
                    viewportItems,
                    hit.PointerOffsetInThumb);
            }

            return true;
        }

        firstVisibleIndex = ScrollBarInteraction.ApplyClick(state, hit.Part);
        return true;
    }
}
