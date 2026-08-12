using CSharpFar.App.CommandLine;

namespace CSharpFar.Tests;

public sealed class CommandLineDisplayTextTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abc\ndef", "abc↵def")]
    [InlineData("a\n\nb", "a↵↵b")]
    public void Format_UsesOneDisplayCellForEachLineFeed(string raw, string displayed)
    {
        Assert.Equal(displayed, CommandLineDisplayText.Format(raw));
        Assert.Equal(raw.Length, displayed.Length);
    }
}
