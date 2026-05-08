using CSharpFar.Console.Input;

namespace CSharpFar.Tests;

public sealed class VirtualTerminalKeyParserTests
{
    [Theory]
    [InlineData('[', "A", ConsoleKey.UpArrow)]
    [InlineData('[', "B", ConsoleKey.DownArrow)]
    [InlineData('[', "C", ConsoleKey.RightArrow)]
    [InlineData('[', "D", ConsoleKey.LeftArrow)]
    [InlineData('[', "21~", ConsoleKey.F10)]
    [InlineData('O', "P", ConsoleKey.F1)]
    public void TryParse_KnownSequence_ReturnsConsoleKey(char prefix, string sequence, ConsoleKey expectedKey)
    {
        bool parsed = VirtualTerminalKeyParser.TryParse(prefix, sequence.ToCharArray(), out var keyInfo);

        Assert.True(parsed);
        Assert.Equal(expectedKey, keyInfo.Key);
        Assert.Equal('\0', keyInfo.KeyChar);
    }

    [Fact]
    public void TryParse_ModifiedArrow_ReturnsModifiers()
    {
        bool parsed = VirtualTerminalKeyParser.TryParse('[', "1;5D".ToCharArray(), out var keyInfo);

        Assert.True(parsed);
        Assert.Equal(ConsoleKey.LeftArrow, keyInfo.Key);
        Assert.True(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
        Assert.False(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.False(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    [Fact]
    public void TryParse_UnknownSequence_ReturnsFalse()
    {
        bool parsed = VirtualTerminalKeyParser.TryParse('[', "999~".ToCharArray(), out _);

        Assert.False(parsed);
    }
}
