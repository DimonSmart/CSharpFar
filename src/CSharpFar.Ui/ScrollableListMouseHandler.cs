using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public static class ScrollableListMouseHandler
{
    public static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        Rect scrollbarBounds,
        int totalItems,
        int viewportItems,
        ref int selectedIndex,
        ref int firstVisibleIndex,
        ref ScrollBarDragState? dragState)
    {
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                scrollbarBounds,
                totalItems,
                viewportItems,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        firstVisibleIndex = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            totalItems,
            viewportItems);

        if (totalItems <= 0)
        {
            selectedIndex = -1;
            firstVisibleIndex = 0;
            return true;
        }

        int lastVisibleIndex = Math.Min(totalItems - 1, firstVisibleIndex + viewportItems - 1);
        selectedIndex = Math.Clamp(selectedIndex, firstVisibleIndex, lastVisibleIndex);
        return true;
    }
}
