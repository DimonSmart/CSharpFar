using System.Text;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Console.Ansi.Tests;

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

    [Theory]
    [InlineData("\x1b[13;5u", ConsoleModifiers.Control)]
    [InlineData("\x1b[13;2u", ConsoleModifiers.Shift)]
    [InlineData("\x1b[13;6u", ConsoleModifiers.Control | ConsoleModifiers.Shift)]
    public void Parse_EnterCombinations_PreserveModifiers(string sequence, ConsoleModifiers modifiers)
    {
        var result = Parse(sequence);

        Assert.True(result.IsKnown);
        Assert.Equal(ConsoleKey.Enter, result.ParsedKey.Key);
        Assert.Equal(modifiers, result.ParsedKey.Modifiers);
    }

    [Fact]
    public void Parse_AssociatedText_UsesTerminalText()
    {
        var result = Parse("\x1b[97;2;65u");

        Assert.True(result.IsKnown);
        Assert.Equal(ConsoleKey.A, result.ParsedKey.Key);
        Assert.Equal(ConsoleModifiers.Shift, result.ParsedKey.Modifiers);
        Assert.Equal('A', result.ParsedKey.KeyChar);
        Assert.Equal("A", result.AssociatedText);
    }

    [Fact]
    public void Parse_PureTextEvent_ReturnsValidText()
    {
        var result = Parse("\x1b[0;;229u");

        Assert.True(result.IsKnown);
        Assert.Equal(ConsoleKey.NoName, result.ParsedKey.Key);
        Assert.Equal('å', result.ParsedKey.KeyChar);
        Assert.Equal("å", result.AssociatedText);
    }

    [Fact]
    public void Parse_MultiCodePointAssociatedText_PreservesAllText()
    {
        var result = Parse("\x1b[97;1;97:769u");

        Assert.True(result.IsKnown);
        Assert.Equal("á", result.AssociatedText);
        Assert.Equal('\0', result.ParsedKey.KeyChar);
    }

    [Fact]
    public void Parse_NonBmpAssociatedText_PreservesSurrogatePair()
    {
        var result = Parse("\x1b[0;;128512u");

        Assert.True(result.IsKnown);
        Assert.Equal("😀", result.AssociatedText);
        Assert.Equal('\0', result.ParsedKey.KeyChar);
    }

    [Theory]
    [InlineData("\x1b[0;;1114112u")]
    [InlineData("\x1b[0;;13u")]
    [InlineData("\x1b[97;x;65u")]
    public void Parse_MalformedAssociatedOrModifierField_ReturnsUnknown(string sequence)
    {
        Assert.False(Parse(sequence).IsKnown);
    }

}
