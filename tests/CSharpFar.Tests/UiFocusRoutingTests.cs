using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiFocusRoutingTests
{
    [Fact]
    public void TryHandleTraversal_TabRequestsNextFocus()
    {
        bool handled = UiFocusRouting.TryHandleTraversal(new KeyConsoleInputEvent(Key(ConsoleKey.Tab)), out UiInputResult result);

        Assert.True(handled);
        Assert.True(result.Handled);
        Assert.True(result.Invalidate);
        Assert.Equal(UiFocusRequest.MoveNext, result.FocusRequest);
    }

    [Fact]
    public void TryHandleTraversal_ShiftTabRequestsPreviousFocus()
    {
        bool handled = UiFocusRouting.TryHandleTraversal(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Tab, shift: true, alt: false, control: false)),
            out UiInputResult result);

        Assert.True(handled);
        Assert.Equal(UiFocusRequest.MovePrevious, result.FocusRequest);
    }

    [Fact]
    public void TryHandleTraversal_OtherKeyIsNotHandled()
    {
        bool handled = UiFocusRouting.TryHandleTraversal(new KeyConsoleInputEvent(Key(ConsoleKey.Enter)), out UiInputResult result);

        Assert.False(handled);
        Assert.Equal(UiInputResult.NotHandled, result);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
