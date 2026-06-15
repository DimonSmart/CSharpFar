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
        var startInfo = CreateBaseStartInfo(_shellExecutable, workingDirectory);
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    internal static ProcessStartInfo CreateBaseStartInfo(string fileName, string workingDirectory) =>
        new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false,
        };
}
