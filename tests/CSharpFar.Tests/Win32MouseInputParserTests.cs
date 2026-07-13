using CSharpFar.Console.Input;
using CSharpFar.Console.Win32;

namespace CSharpFar.Tests;

public sealed class Win32MouseInputParserTests
{
    private const uint Left = 0x0001;
    private const uint Right = 0x0002;
    private const uint Middle = 0x0004;
    private const uint Shift = 0x0010;
    private const uint MouseMoved = 0x0001;
    private const uint DoubleClick = 0x0002;
    private const uint MouseWheeled = 0x0004;

    [Theory]
    [InlineData(Left, MouseButton.Left)]
    [InlineData(Right, MouseButton.Right)]
    [InlineData(Middle, MouseButton.Middle)]
    public void ButtonTransitions_ReturnDownAndUp(uint buttonState, MouseButton button)
    {
        var parser = new Win32MouseInputParser();

        var down = parser.Parse(Record(buttonState), windowLeft: 10, windowTop: 20);
        var up = parser.Parse(Record(0), windowLeft: 10, windowTop: 20);

        AssertMouse(down, button, MouseEventKind.Down);
        AssertMouse(up, button, MouseEventKind.Up);
    }

    [Fact]
    public void MoveWithPressedButton_ReturnsMove()
    {
        var parser = new Win32MouseInputParser();
        parser.Parse(Record(Left), 0, 0);

        var move = parser.Parse(Record(Left, MouseMoved), 0, 0);

        AssertMouse(move, MouseButton.Left, MouseEventKind.Move);
    }

    [Fact]
    public void HoverMoveWithoutPressedButton_IsIgnored()
    {
        var parser = new Win32MouseInputParser();

        var move = parser.Parse(Record(0, MouseMoved), 0, 0);

        Assert.Null(move);
    }

    [Theory]
    [InlineData(120u << 16, MouseButton.WheelUp)]
    [InlineData(0xFF88u << 16, MouseButton.WheelDown)]
    public void WheelDelta_ReturnsWheel(uint buttonState, MouseButton button)
    {
        var parser = new Win32MouseInputParser();

        var wheel = parser.Parse(Record(buttonState, MouseWheeled), 0, 0);

        AssertMouse(wheel, button, MouseEventKind.Wheel);
    }

    [Fact]
    public void CoordinatesAreViewportRelativeAndModifiersPreserved()
    {
        var parser = new Win32MouseInputParser();

        var down = parser.Parse(Record(Left, 0, x: 12, y: 23, controlKeyState: Shift), 10, 20);

        Assert.NotNull(down);
        Assert.Equal((2, 3), (down.X, down.Y));
        Assert.Equal(MouseKeyModifiers.Shift, down.Modifiers);
    }

    [Fact]
    public void NativeDoubleClick_IsPhysicalSecondDownForNormalizer()
    {
        var parser = new Win32MouseInputParser();
        parser.Parse(Record(Left), 0, 0);
        parser.Parse(Record(0), 0, 0);

        var secondDown = parser.Parse(Record(Left, DoubleClick), 0, 0);

        AssertMouse(secondDown, MouseButton.Left, MouseEventKind.Down);
    }

    [Fact]
    public void ReleaseOneButtonWhileAnotherIsHeld_PreservesState()
    {
        var parser = new Win32MouseInputParser();
        parser.Parse(Record(Left | Right), 0, 0);

        var releaseLeft = parser.Parse(Record(Right), 0, 0);
        var releaseRight = parser.Parse(Record(0), 0, 0);

        AssertMouse(releaseLeft, MouseButton.Left, MouseEventKind.Up);
        AssertMouse(releaseRight, MouseButton.Right, MouseEventKind.Up);
    }

    [Fact]
    public void MultipleTransitionsUseDeterministicPriorityAndUpdateFullState()
    {
        var parser = new Win32MouseInputParser();

        var first = parser.Parse(Record(Left | Right), 0, 0);
        var second = parser.Parse(Record(0), 0, 0);

        AssertMouse(first, MouseButton.Left, MouseEventKind.Down);
        AssertMouse(second, MouseButton.Left, MouseEventKind.Up);
    }

    private static MouseEventRecord Record(
        uint buttonState,
        uint eventFlags = 0,
        short x = 1,
        short y = 2,
        uint controlKeyState = 0) =>
        new()
        {
            MousePositionX = x,
            MousePositionY = y,
            ButtonState = buttonState,
            EventFlags = eventFlags,
            ControlKeyState = controlKeyState,
        };

    private static void AssertMouse(
        MouseConsoleInputEvent? mouse,
        MouseButton button,
        MouseEventKind kind)
    {
        Assert.NotNull(mouse);
        Assert.Equal(button, mouse.Button);
        Assert.Equal(kind, mouse.Kind);
    }
}
