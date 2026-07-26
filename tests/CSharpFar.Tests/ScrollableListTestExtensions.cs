using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ScrollableListTestExtensions
{
    public static ScrollableListInputResult HandleMouse<T>(
        this ScrollableList<T> list,
        MouseConsoleInputEvent mouse,
        Rect contentBounds,
        Rect? scrollbarBounds,
        int viewportRows,
        ref ScrollBarDragState? drag,
        bool confirmOnMouseDown = false,
        bool confirmOnDoubleClick = true)
    {
        ScrollableListFrameState frame = list.CalculateFrameState(viewportRows, scrollbarBounds);
        list.ApplyCommittedFrame(frame);
        bool isScrollbarTarget = scrollbarBounds is Rect bounds && bounds.Contains(mouse.X, mouse.Y);
        bool isContentTarget = contentBounds.Contains(mouse.X, mouse.Y);
        ScrollableListInputResult result = mouse.Kind == MouseEventKind.Wheel
            ? isContentTarget || isScrollbarTarget
                ? list.HandleContentMouse(mouse, contentBounds, frame, confirmOnMouseDown, confirmOnDoubleClick)
                : ScrollableListInputResult.NotHandled
            : drag is not null || isScrollbarTarget
                ? list.HandleScrollbarMouse(mouse, frame)
                : isContentTarget
                    ? list.HandleContentMouse(mouse, contentBounds, frame, confirmOnMouseDown, confirmOnDoubleClick)
                    : ScrollableListInputResult.NotHandled;
        drag = list.ScrollbarDragState;
        return result;
    }

    public static ScrollableListInputResult HandleMouse<T>(
        this ScrollableList<T> list,
        MouseConsoleInputEvent mouse,
        Rect contentBounds,
        Rect? scrollbarBounds,
        int viewportRows,
        bool confirmOnMouseDown = false,
        bool confirmOnDoubleClick = true)
    {
        ScrollBarDragState? drag = null;
        return list.HandleMouse(mouse, contentBounds, scrollbarBounds, viewportRows, ref drag, confirmOnMouseDown, confirmOnDoubleClick);
    }

    public static ScrollBarDragState? GetScrollbarDrag<T>(this ScrollableList<T> list) => list.ScrollbarDragState;
}
