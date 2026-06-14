using System.Text;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Tests;

public sealed class AnsiInputParserTests
{
    [Theory]
    [InlineData("\u001b[A", ConsoleKey.UpArrow)]
    [InlineData("\u001b[B", ConsoleKey.DownArrow)]
    [InlineData("\u001b[C", ConsoleKey.RightArrow)]
    [InlineData("\u001b[D", ConsoleKey.LeftArrow)]
    [InlineData("\u001b[H", ConsoleKey.Home)]
    [InlineData("\u001b[1~", ConsoleKey.Home)]
    [InlineData("\u001b[F", ConsoleKey.End)]
    [InlineData("\u001b[4~", ConsoleKey.End)]
    [InlineData("\u001b[3~", ConsoleKey.Delete)]
    public void ParseSingle_MapsEscapeSequences(string sequence, ConsoleKey expected)
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes(sequence));

        Assert.Equal(expected, key.Key);
    }

    [Fact]
    public void ParseSingle_MapsUtf8Character()
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes("Ж"));

        Assert.Equal('Ж', key.KeyChar);
    }

    [Fact]
    public void ParseSingle_MapsCtrlLetter()
    {
        var key = AnsiInputParser.ParseSingle([0x03]);

        Assert.Equal(ConsoleKey.C, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }
}
