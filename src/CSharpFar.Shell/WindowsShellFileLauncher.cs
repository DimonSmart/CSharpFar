using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class WindowsShellFileLauncher : IFileLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public WindowsShellFileLauncher()
        : this(Process.Start)
    {
    }

    internal WindowsShellFileLauncher(Func<ProcessStartInfo, Process?> startProcess) =>
        _startProcess = startProcess;

    public void OpenFile(string fullPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            Verb = "open",
        };

        using var process = _startProcess(startInfo);
    }
}
