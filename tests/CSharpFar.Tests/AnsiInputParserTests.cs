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
    [InlineData("\u001bOA", ConsoleKey.UpArrow)]
    [InlineData("\u001bOB", ConsoleKey.DownArrow)]
    [InlineData("\u001bOC", ConsoleKey.RightArrow)]
    [InlineData("\u001bOD", ConsoleKey.LeftArrow)]
    [InlineData("\u001b[H", ConsoleKey.Home)]
    [InlineData("\u001b[1~", ConsoleKey.Home)]
    [InlineData("\u001b[F", ConsoleKey.End)]
    [InlineData("\u001b[4~", ConsoleKey.End)]
    [InlineData("\u001b[3~", ConsoleKey.Delete)]
    [InlineData("\u001bOP", ConsoleKey.F1)]
    [InlineData("\u001bOQ", ConsoleKey.F2)]
    [InlineData("\u001bOR", ConsoleKey.F3)]
    [InlineData("\u001bOS", ConsoleKey.F4)]
    [InlineData("\u001b[11~", ConsoleKey.F1)]
    [InlineData("\u001b[12~", ConsoleKey.F2)]
    [InlineData("\u001b[13~", ConsoleKey.F3)]
    [InlineData("\u001b[14~", ConsoleKey.F4)]
    [InlineData("\u001b[[A", ConsoleKey.F1)]
    [InlineData("\u001b[[B", ConsoleKey.F2)]
    [InlineData("\u001b[[C", ConsoleKey.F3)]
    [InlineData("\u001b[[D", ConsoleKey.F4)]
    [InlineData("\u001b[[E", ConsoleKey.F5)]
    [InlineData("\u001b[15~", ConsoleKey.F5)]
    [InlineData("\u001b[17~", ConsoleKey.F6)]
    [InlineData("\u001b[18~", ConsoleKey.F7)]
    [InlineData("\u001b[19~", ConsoleKey.F8)]
    [InlineData("\u001b[20~", ConsoleKey.F9)]
    [InlineData("\u001b[21~", ConsoleKey.F10)]
    [InlineData("\u001b[23~", ConsoleKey.F11)]
    [InlineData("\u001b[24~", ConsoleKey.F12)]
    public void ParseSingle_MapsEscapeSequences(string sequence, ConsoleKey expected)
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes(sequence));

        Assert.Equal(expected, key.Key);
    }

    [Theory]
    [InlineData("\u001b[1;5C", ConsoleKey.RightArrow, ConsoleModifiers.Control)]
    [InlineData("\u001b[1;2D", ConsoleKey.LeftArrow, ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;6A", ConsoleKey.UpArrow, ConsoleModifiers.Control | ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;3B", ConsoleKey.DownArrow, ConsoleModifiers.Alt)]
    [InlineData("\u001b[15;2~", ConsoleKey.F5, ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;5P", ConsoleKey.F1, ConsoleModifiers.Control)]
    [InlineData("\u001b[Z", ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("\u001b1", ConsoleKey.D1, ConsoleModifiers.Alt)]
    [InlineData("\u001bo", ConsoleKey.O, ConsoleModifiers.Alt)]
    [InlineData("\u001b\u001b[D", ConsoleKey.LeftArrow, ConsoleModifiers.Alt)]
    [InlineData("\u000f", ConsoleKey.O, ConsoleModifiers.Control)]
    public void ParseSingle_MapsModifiers(string sequence, ConsoleKey expectedKey, ConsoleModifiers expectedModifiers)
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes(sequence));

        Assert.Equal(expectedKey, key.Key);
        Assert.Equal(expectedModifiers, key.Modifiers);
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

    [Fact]
    public void ParseSingle_MapsStandaloneEscape()
    {
        var key = AnsiInputParser.ParseSingle([0x1b]);

        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.Equal('\x1b', key.KeyChar);
    }

    [Fact]
    public void Read_ReturnsRawBytes()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("\u001b[A"));

        var result = new AnsiInputParser().Read(input);

        Assert.Equal(ConsoleKey.UpArrow, result.Key.Key);
        Assert.Equal([0x1b, 0x5b, 0x41], result.Bytes);
    }
}
