using CSharpFar.Console.Input;

namespace CSharpFar.Tests;

public sealed class MouseInputNormalizerTests
{
    private long _timestamp;

    [Fact]
    public void FirstDown_RemainsDown()
    {
        var normalizer = Create();

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.Down, result.Kind);
    }

    [Fact]
    public void MatchingSecondDownWithinInterval_BecomesDoubleClick()
    {
        var normalizer = Create();
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        _timestamp += 499;

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.DoubleClick, result.Kind);
    }

    [Theory]
    [InlineData(501, MouseButton.Left, 1, 2, MouseKeyModifiers.None)]
    [InlineData(100, MouseButton.Right, 1, 2, MouseKeyModifiers.None)]
    [InlineData(100, MouseButton.Left, 2, 2, MouseKeyModifiers.None)]
    [InlineData(100, MouseButton.Left, 1, 3, MouseKeyModifiers.None)]
    [InlineData(100, MouseButton.Left, 1, 2, MouseKeyModifiers.Shift)]
    public void NonMatchingSecondDown_RemainsDown(
        long elapsed,
        MouseButton button,
        int x,
        int y,
        MouseKeyModifiers modifiers)
    {
        var normalizer = Create();
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        _timestamp += elapsed;

        var result = normalizer.Normalize(Mouse(button, MouseEventKind.Down, x, y, modifiers));

        Assert.Equal(MouseEventKind.Down, result.Kind);
    }

    [Fact]
    public void Up_DoesNotResetCandidate()
    {
        var normalizer = Create();
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Up));
        _timestamp += 100;

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.DoubleClick, result.Kind);
    }

    [Theory]
    [InlineData(MouseEventKind.Move, MouseButton.Left)]
    [InlineData(MouseEventKind.Wheel, MouseButton.WheelUp)]
    public void MoveAndWheel_ResetCandidate(MouseEventKind kind, MouseButton button)
    {
        var normalizer = Create();
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        normalizer.Normalize(Mouse(button, kind));
        _timestamp += 100;

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.Down, result.Kind);
    }

    [Fact]
    public void AfterDoubleClick_NextDownStartsNewSequence()
    {
        var normalizer = Create();
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        _timestamp += 100;
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        _timestamp += 100;

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.Down, result.Kind);
    }

    [Fact]
    public void NegativeTimestampDifference_DoesNotDoubleClick()
    {
        var normalizer = Create();
        _timestamp = 100;
        normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));
        _timestamp = 99;

        var result = normalizer.Normalize(Mouse(MouseButton.Left, MouseEventKind.Down));

        Assert.Equal(MouseEventKind.Down, result.Kind);
    }

    private MouseInputNormalizer Create() =>
        new(() => _timestamp);

    private static MouseConsoleInputEvent Mouse(
        MouseButton button,
        MouseEventKind kind,
        int x = 1,
        int y = 2,
        MouseKeyModifiers modifiers = MouseKeyModifiers.None) =>
        new(x, y, button, kind, modifiers);
}
