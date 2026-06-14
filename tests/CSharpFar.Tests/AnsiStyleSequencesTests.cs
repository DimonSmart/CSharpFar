using CSharpFar.Console.Ansi;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests;

public sealed class AnsiStyleSequencesTests
{
    [Theory]
    [InlineData(ConsoleColor.Black, 30, 40)]
    [InlineData(ConsoleColor.DarkRed, 31, 41)]
    [InlineData(ConsoleColor.DarkGreen, 32, 42)]
    [InlineData(ConsoleColor.DarkYellow, 33, 43)]
    [InlineData(ConsoleColor.DarkBlue, 34, 44)]
    [InlineData(ConsoleColor.DarkMagenta, 35, 45)]
    [InlineData(ConsoleColor.DarkCyan, 36, 46)]
    [InlineData(ConsoleColor.Gray, 37, 47)]
    [InlineData(ConsoleColor.DarkGray, 90, 100)]
    [InlineData(ConsoleColor.Red, 91, 101)]
    [InlineData(ConsoleColor.Green, 92, 102)]
    [InlineData(ConsoleColor.Yellow, 93, 103)]
    [InlineData(ConsoleColor.Blue, 94, 104)]
    [InlineData(ConsoleColor.Magenta, 95, 105)]
    [InlineData(ConsoleColor.Cyan, 96, 106)]
    [InlineData(ConsoleColor.White, 97, 107)]
    public void ColorCodes_MapConsoleColorsToAnsiSgr(ConsoleColor color, int foreground, int background)
    {
        Assert.Equal(foreground, AnsiStyleSequences.ForegroundCode(color));
        Assert.Equal(background, AnsiStyleSequences.BackgroundCode(color));
    }

    [Fact]
    public void BuildSgr_IncludesAttributesAndColors()
    {
        string sgr = AnsiStyleSequences.BuildSgr(
            ConsoleColor.Yellow,
            ConsoleColor.DarkBlue,
            TextAttributes.Bold | TextAttributes.Underline | TextAttributes.Reverse);

        Assert.Equal("\x1b[0;1;4;7;93;44m", sgr);
    }
}
