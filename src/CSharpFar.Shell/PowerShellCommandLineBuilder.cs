using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class PowerShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly Func<string?> _resolveExecutable;

    public PowerShellCommandLineBuilder(Func<string?>? resolveExecutable = null) =>
        _resolveExecutable = resolveExecutable ?? ResolveExecutable;

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        string executable = _resolveExecutable() ?? throw new FileNotFoundException(
            "PowerShell executable was not found. Tried: pwsh.exe, powershell.exe");
        var startInfo = ShellProcessStartInfoFactory.Create(executable, workingDirectory);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static string? ResolveExecutable()
    {
        foreach (string candidate in new[] { "pwsh.exe", "powershell.exe" })
        {
            if (FindOnPath(candidate) is { } path)
                return path;
        }

        return null;
    }

    private static string? FindOnPath(string executable)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
