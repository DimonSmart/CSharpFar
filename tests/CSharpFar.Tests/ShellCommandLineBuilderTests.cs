using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class ShellCommandLineBuilderTests
{
    [Fact]
    public void WindowsBuilder_UsesArgumentList()
    {
        string command = "echo \"hello world\" && set A=$B";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Equal(["/d", "/c", command], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }

    [Fact]
    public void WindowsBuilder_PreservesQuotedCommandArguments()
    {
        string command = "git commit -m \"Initial commit\"";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Equal(["/d", "/c", command], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }

    [Fact]
    public void WindowsBuilder_DoesNotWrapWholeCommandAsExecutableName()
    {
        string command = "npm run package";
        var startInfo = new WindowsShellCommandLineBuilder().CreateStartInfo(command, "C:\\");

        Assert.Equal(["/d", "/c", command], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }

    [Fact]
    public void UnixBuilder_UsesArgumentList()
    {
        string command = "printf '%s\\n' \"a b\"; echo $HOME";
        var startInfo = new UnixShellCommandLineBuilder().CreateStartInfo(command, "/tmp");

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal(["-c", command], startInfo.ArgumentList);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
    }
}
