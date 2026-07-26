using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

/// <summary>Converts standard focus traversal keys into focus requests.</summary>
public static class UiFocusRouting
{
    public static bool TryHandleTraversal(ConsoleInputEvent input, out UiInputResult result)
    {
        if (input is not KeyConsoleInputEvent { Key.Key: ConsoleKey.Tab, Key.Modifiers: var modifiers })
        {
            result = UiInputResult.NotHandled;
            return false;
        }

        UiFocusRequest request = (modifiers & ConsoleModifiers.Shift) != 0
            ? UiFocusRequest.MovePrevious
            : UiFocusRequest.MoveNext;
        result = new UiInputResult(true, true, request, UiMouseCaptureRequest.None);
        return true;
    }
}
