using System.Text;
using CSharpFar.Console.Ansi;
using CSharpFar.Console.Input;

namespace CSharpFar.Tests;

public sealed class TerminalInputLabParserTests
{
    [Theory]
    [InlineData("\x1b[A", ConsoleKey.UpArrow, ConsoleModifiers.None)]
    [InlineData("\x1b[B", ConsoleKey.DownArrow, ConsoleModifiers.None)]
    [InlineData("\x1b[C", ConsoleKey.RightArrow, ConsoleModifiers.None)]
    [InlineData("\x1b[D", ConsoleKey.LeftArrow, ConsoleModifiers.None)]
    [InlineData("\x1bOP", ConsoleKey.F1, ConsoleModifiers.None)]
    [InlineData("\x1bOQ", ConsoleKey.F2, ConsoleModifiers.None)]
    [InlineData("\x1bOR", ConsoleKey.F3, ConsoleModifiers.None)]
    [InlineData("\x1bOS", ConsoleKey.F4, ConsoleModifiers.None)]
    [InlineData("\x1b[15~", ConsoleKey.F5, ConsoleModifiers.None)]
    [InlineData("\x1b[24~", ConsoleKey.F12, ConsoleModifiers.None)]
    [InlineData("\x1b[Z", ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("\u001ba", ConsoleKey.A, ConsoleModifiers.Alt)]
    [InlineData("\x1b[1;3D", ConsoleKey.LeftArrow, ConsoleModifiers.Alt)]
    [InlineData("\x1b[1;5C", ConsoleKey.RightArrow, ConsoleModifiers.Control)]
    [InlineData("\x1b[15;2~", ConsoleKey.F5, ConsoleModifiers.Shift)]
    public void Parse_KeySequences_PreservesRawAndMapsKey(string sequence, ConsoleKey key, ConsoleModifiers modifiers)
    {
        byte[] raw = Encoding.ASCII.GetBytes(sequence);

        var result = new TerminalInputLabParser().Parse(raw);

        Assert.Equal("Key", result.Kind);
        Assert.True(result.IsKnown);
        Assert.Equal(key, result.Key?.Key);
        Assert.Equal(modifiers, result.Key?.Modifiers);
        Assert.Equal(raw, result.RawBytes);
    }

    [Theory]
    [InlineData("\x1b[<0;42;10M", "LeftDown", MouseButton.Left, 41, 9)]
    [InlineData("\x1b[<0;42;10m", "LeftUp", MouseButton.Left, 41, 9)]
    [InlineData("\x1b[<64;42;10M", "WheelUp", MouseButton.WheelUp, 41, 9)]
    [InlineData("\x1b[<65;42;10M", "WheelDown", MouseButton.WheelDown, 41, 9)]
    public void Parse_MouseSequences_MapsEventAndCoordinates(string sequence, string mouseEvent, MouseButton button, int x, int y)
    {
        byte[] raw = Encoding.ASCII.GetBytes(sequence);

        var result = new TerminalInputLabParser().Parse(raw);

        Assert.Equal("Mouse", result.Kind);
        Assert.True(result.IsKnown);
        Assert.Equal(mouseEvent, result.MouseEvent);
        Assert.Equal(button, result.MouseButton);
        Assert.Equal(42, result.TerminalX);
        Assert.Equal(10, result.TerminalY);
        Assert.Equal(x, result.UiX);
        Assert.Equal(y, result.UiY);
        Assert.Equal(raw, result.RawBytes);
    }

    [Fact]
    public void Parse_MalformedMouse_ReturnsMalformedWithRawSequence()
    {
        byte[] raw = Encoding.ASCII.GetBytes("\x1b[<x;y;zM");

        var result = new TerminalInputLabParser().Parse(raw);

        Assert.Equal("MalformedMouse", result.Kind);
        Assert.False(result.IsKnown);
        Assert.NotNull(result.Error);
        Assert.Equal(raw, result.RawBytes);
    }

    [Fact]
    public void Parse_UnknownEscape_ReturnsUnknownWithRawSequence()
    {
        byte[] raw = Encoding.ASCII.GetBytes("\x1b[?9999h");

        var result = new TerminalInputLabParser().Parse(raw);

        Assert.Equal("UnknownEscapeSequence", result.Kind);
        Assert.False(result.IsKnown);
        Assert.Equal(raw, result.RawBytes);
    }
}
