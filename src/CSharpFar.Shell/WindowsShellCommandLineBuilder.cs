using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class WindowsShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly string _shellExecutable;

    public WindowsShellCommandLineBuilder(string shellExecutable = "cmd.exe")
    {
        _shellExecutable = string.IsNullOrWhiteSpace(shellExecutable) ? "cmd.exe" : shellExecutable;
    }

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var startInfo = ShellProcessStartInfoFactory.Create(_shellExecutable, workingDirectory);
        startInfo.Arguments = "/d /s /c \"" + command + "\"";
        return startInfo;
    }

}
