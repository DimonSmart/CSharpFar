using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

internal static class ScrollableViewportRouting
{
    public static UiInputResult ToUiInputResult(
        ScrollableViewportInputResult result,
        UiTargetId scrollbarTarget)
    {
        if (!result.IsHandled)
            return UiInputResult.NotHandled;
        if (result.DragStarted)
            return UiInputResult.CaptureMouse(scrollbarTarget, MouseButton.Left, result.PositionChanged);
        if (result.DragEnded)
            return UiInputResult.ReleaseMouse(result.PositionChanged);
        return result.PositionChanged ? UiInputResult.HandledAndInvalidate : UiInputResult.HandledResult;
    }
}
