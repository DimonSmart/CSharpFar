using System.ComponentModel;
using System.Diagnostics;

namespace CSharpFar.Shell;

public sealed class MacOsAssociationLauncher : IUnixAssociationLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public MacOsAssociationLauncher() : this(Process.Start)
    {
    }

    internal MacOsAssociationLauncher(Func<ProcessStartInfo, Process?> startProcess) => _startProcess = startProcess;

    public bool TryOpen(string fullPath, string workingDirectory, out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(fullPath);
            using Process? process = _startProcess(startInfo);
            if (process is null)
            {
                error = "Cannot start macOS open.";
                return false;
            }
            if (!process.WaitForExit(750) || process.ExitCode == 0)
            {
                error = null;
                return true;
            }
            error = $"macOS open exited with code {process.ExitCode}.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            error = $"Cannot start macOS open: {ex.Message}";
            return false;
        }
    }
}
