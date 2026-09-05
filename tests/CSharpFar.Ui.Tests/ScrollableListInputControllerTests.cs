using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ScrollableListInputControllerTests
{
    [Fact]
    public void KeyboardNavigation_UsesTheCommittedFrameAndClampsAtBounds()
    {
        var state = new ScrollableListState<int>([1, 2, 3]);
        var input = new ScrollableListInputController();
        ScrollableListFrame frame = input.CalculateFrame(state, new Rect(0, 0, 8, 1), null);

        Assert.Equal(ScrollableListInputResultKind.Handled, input.HandleKey(state, frame, Key(ConsoleKey.UpArrow)).Kind);
        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, input.HandleKey(state, frame, Key(ConsoleKey.End)).Kind);
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(ScrollableListInputResultKind.Confirmed, input.HandleKey(state, frame, Key(ConsoleKey.Enter)).Kind);
    }

    [Fact]
    public void MouseSelection_RespectsSingleAndDoubleClickConfirmationPolicies()
    {
        var state = new ScrollableListState<int>([1, 2]);
        var input = new ScrollableListInputController();
        ScrollableListFrame frame = input.CalculateFrame(state, new Rect(0, 0, 8, 2), null);

        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, input.HandleContentMouse(state, frame, Mouse(0, 1, MouseEventKind.Down)).Kind);
        Assert.Equal(1, state.SelectedIndex);
        Assert.Equal(ScrollableListInputResultKind.Confirmed, input.HandleContentMouse(state, frame, Mouse(0, 1, MouseEventKind.DoubleClick)).Kind);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
    private static MouseConsoleInputEvent Mouse(int x, int y, MouseEventKind kind) => new(x, y, MouseButton.Left, kind, MouseKeyModifiers.None);
}
