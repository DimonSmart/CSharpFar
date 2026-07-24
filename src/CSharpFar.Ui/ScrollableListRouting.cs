using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public static class ScrollableListRouting
{
    public static UiInputResult ToUiInputResult(
        ScrollableListInputResult result,
        UiTargetId scrollbarTarget)
    {
        if (!result.IsHandled)
            return UiInputResult.NotHandled;
        if (result.DragStarted)
            return UiInputResult.CaptureMouse(scrollbarTarget, MouseButton.Left, invalidate: true);
        if (result.DragEnded)
            return UiInputResult.ReleaseMouse(invalidate: true);

        return result.Kind is ScrollableListInputResultKind.SelectionChanged or ScrollableListInputResultKind.Confirmed
            ? UiInputResult.HandledAndInvalidate
            : UiInputResult.HandledResult;
    }
}
