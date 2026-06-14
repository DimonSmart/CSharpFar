using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class UnixShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly string _shellExecutable;

    public UnixShellCommandLineBuilder(string shellExecutable = "/bin/sh")
    {
        _shellExecutable = string.IsNullOrWhiteSpace(shellExecutable) ? "/bin/sh" : shellExecutable;
    }

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        var startInfo = WindowsShellCommandLineBuilder.CreateBaseStartInfo(_shellExecutable, workingDirectory);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }
}
