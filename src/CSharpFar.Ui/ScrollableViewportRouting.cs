using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public static class ScrollableViewportRouting
{
    public static UiInputResult ToUiInputResult(
        ScrollableViewportInputResult result,
        UiTargetId scrollbarTarget)
        => VerticalScrollbarRouting.ToUiInputResult(
            new VerticalScrollbarInputResult(
                result.IsHandled,
                FirstVisibleIndex: 0,
                result.PositionChanged,
                result.DragStarted,
                result.DragEnded),
            scrollbarTarget);
}
