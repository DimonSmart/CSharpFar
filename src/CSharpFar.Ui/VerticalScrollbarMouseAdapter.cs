using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

internal static class VerticalScrollbarMouseAdapter
{
    public static bool TryHandleMouse(
        MouseConsoleInputEvent mouse,
        Rect bounds,
        int totalItems,
        int viewportItems,
        ref int firstVisibleIndex,
        ref ScrollBarDragState? dragState)
    {
        var controller = new VerticalScrollbarController();
        controller.ApplyCommittedFrame(new VerticalScrollbarFrame(
            bounds,
            totalItems,
            viewportItems,
            firstVisibleIndex,
            dragState));
        VerticalScrollbarFrame? frame = controller.CalculateFrame(bounds, new ScrollState
        {
            TotalItems = totalItems,
            ViewportItems = viewportItems,
            FirstVisibleIndex = firstVisibleIndex,
        });
        if (frame is null)
            return false;

        VerticalScrollbarInputResult result = controller.HandleMouse(mouse, frame.Value);
        if (!result.IsHandled)
            return false;

        firstVisibleIndex = result.FirstVisibleIndex;
        dragState = controller.DragState;
        return true;
    }
}
