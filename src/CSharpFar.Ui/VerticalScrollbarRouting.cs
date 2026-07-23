using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public static class VerticalScrollbarRouting
{
    public static UiInputResult ToUiInputResult(
        VerticalScrollbarInputResult result,
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
