using CSharpFar.App.CommandLine;

namespace CSharpFar.Tests;

public sealed class ChangeDirectoryCommandParserTests
{
    [Theory]
    [InlineData("cd C:\\Temp", "C:\\Temp")]
    [InlineData("chdir C:\\Temp", "C:\\Temp")]
    [InlineData("cd /d C:\\Temp", "C:\\Temp")]
    [InlineData("cd \"C:\\Program Files\"", "C:\\Program Files")]
    public void TryParseTargetAcceptsChangeDirectoryCommands(string command, string expected)
    {
        bool parsed = ChangeDirectoryCommandParser.TryParseTarget(command, out string target);

        Assert.True(parsed);
        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dir")]
    [InlineData("cd")]
    [InlineData("cd C:\\Temp & dir")]
    [InlineData("cd C:\\Temp | more")]
    [InlineData("cd C:\\Temp > out.txt")]
    public void TryParseTargetRejectsNonStandaloneChangeDirectoryCommands(string command)
    {
        bool parsed = ChangeDirectoryCommandParser.TryParseTarget(command, out string target);

        Assert.False(parsed);
        Assert.Equal(string.Empty, target);
    }

    [Fact]
    public void TryParseTargetAllowsSeparatorsInsideQuotes()
    {
        bool parsed = ChangeDirectoryCommandParser.TryParseTarget("cd \"C:\\A&B\"", out string target);

        Assert.True(parsed);
        Assert.Equal("C:\\A&B", target);
    }
}
