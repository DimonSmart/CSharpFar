using System.ComponentModel;
using System.Diagnostics;

namespace CSharpFar.Shell;

public sealed class UnixAssociationLauncher : IUnixAssociationLauncher
{
    private readonly IUnixEnvironment _environment;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public UnixAssociationLauncher(IUnixEnvironment environment)
        : this(environment, Process.Start)
    {
    }

    internal UnixAssociationLauncher(IUnixEnvironment environment, Func<ProcessStartInfo, Process?> startProcess)
    {
        _environment = environment;
        _startProcess = startProcess;
    }

    public bool TryOpen(string fullPath, string workingDirectory, out string? error)
    {
        var errors = new List<string>();
        foreach (var candidate in GetCandidates())
        {
            var startInfo = WindowsShellCommandLineBuilder.CreateBaseStartInfo(candidate.FileName, workingDirectory);
            foreach (string argument in candidate.Arguments)
                startInfo.ArgumentList.Add(argument == "{path}" ? fullPath : argument);

            try
            {
                using var process = _startProcess(startInfo);
                if (process is null)
                {
                    errors.Add($"{candidate.DisplayName}: failed to start.");
                    continue;
                }

                if (!process.WaitForExit(750))
                {
                    error = null;
                    return true;
                }

                if (process.ExitCode == 0)
                {
                    error = null;
                    return true;
                }

                errors.Add($"{candidate.DisplayName}: exit code {process.ExitCode}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                errors.Add($"{candidate.DisplayName}: {ex.Message}");
            }
        }

        error = "Cannot open file with xdg-open, gio open, wslview, or WSL explorer.exe. " + string.Join(" ", errors);
        return false;
    }

    private IEnumerable<Candidate> GetCandidates()
    {
        yield return new Candidate("xdg-open", "xdg-open", ["{path}"]);
        yield return new Candidate("gio open", "gio", ["open", "{path}"]);
        yield return new Candidate("wslview", "wslview", ["{path}"]);
        if (_environment.IsWsl)
            yield return new Candidate("explorer.exe", "explorer.exe", ["{path}"]);
    }

    private sealed record Candidate(string DisplayName, string FileName, string[] Arguments);
}
