using System.Text;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Tests;

public sealed class EnhancedTerminalKeyParserTests
{
    [Fact]
    public void Parse_CsiUWithEventType_ReturnsCtrlPress()
    {
        var result = Parse("\x1b[99;5:1u");

        Assert.True(result.IsKnown);
        Assert.Equal(99, result.KeyCode);
        Assert.Equal(5, result.ModifiersRaw);
        Assert.Equal(EnhancedKeyEventType.Press, result.EventType);
        Assert.True(result.Modifiers.HasFlag(EnhancedModifiers.Ctrl));
        Assert.Equal(ConsoleKey.C, result.ParsedKey.Key);
        Assert.Equal(ConsoleModifiers.Control, result.ParsedKey.Modifiers);
        Assert.False(result.ModifierOnly);
    }

    [Fact]
    public void Parse_CsiURelease_ReturnsRelease()
    {
        var result = Parse("\x1b[99;1:3u");

        Assert.True(result.IsKnown);
        Assert.Equal(EnhancedKeyEventType.Release, result.EventType);
        Assert.Equal(ConsoleModifiers.None, result.ParsedKey.Modifiers);
    }

    [Fact]
    public void Parse_ModifierOnlyControl_ReturnsModifierEvent()
    {
        var result = Parse("\x1b[57442;5:1u");

        Assert.True(result.IsKnown);
        Assert.True(result.ModifierOnly);
        Assert.Equal("LEFT_CONTROL", result.ModifierKeyName);
        Assert.Equal(EnhancedKeyEventType.Press, result.EventType);
        Assert.True(result.Modifiers.HasFlag(EnhancedModifiers.Ctrl));
    }

    [Fact]
    public void Parse_ModifierOnlyControlRelease_ReturnsReleaseWithoutCtrlBit()
    {
        var result = Parse("\x1b[57442;1:3u");

        Assert.True(result.IsKnown);
        Assert.True(result.ModifierOnly);
        Assert.Equal("LEFT_CONTROL", result.ModifierKeyName);
        Assert.Equal(EnhancedKeyEventType.Release, result.EventType);
        Assert.False(result.Modifiers.HasFlag(EnhancedModifiers.Ctrl));
    }

    [Theory]
    [InlineData("\x1b[1;5:1C", ConsoleKey.RightArrow, ConsoleModifiers.Control, 0)]
    [InlineData("\x1b[15;2:1~", ConsoleKey.F5, ConsoleModifiers.Shift, 0)]
    [InlineData("\x1b[1;3:3D", ConsoleKey.LeftArrow, ConsoleModifiers.Alt, 2)]
    public void Parse_EnhancedLegacyForms_ReturnsKeyAndModifiers(
        string sequence,
        ConsoleKey expectedKey,
        ConsoleModifiers expectedModifiers,
        int expectedEventType)
    {
        var result = Parse(sequence);

        Assert.True(result.IsKnown);
        Assert.Equal(expectedKey, result.ParsedKey.Key);
        Assert.Equal(expectedModifiers, result.ParsedKey.Modifiers);
        Assert.Equal((EnhancedKeyEventType)expectedEventType, result.EventType);
    }

    [Fact]
    public void Parse_UnknownSequence_ReturnsUnknown()
    {
        var result = Parse("\x1b[?11u");

        Assert.False(result.IsKnown);
    }

    private static EnhancedTerminalKeyEvent Parse(string sequence) =>
        EnhancedTerminalKeyParser.Parse(Encoding.ASCII.GetBytes(sequence));
}
