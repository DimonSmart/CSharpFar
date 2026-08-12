using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class PowerShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly Func<string?> _resolveExecutable;
    private readonly IReadOnlyList<string> _candidates;

    public PowerShellCommandLineBuilder(Func<string?>? resolveExecutable = null)
        : this(resolveExecutable, PowerShellExecutableCandidates.ForCurrentPlatform)
    {
    }

    internal PowerShellCommandLineBuilder(Func<string?>? resolveExecutable, IReadOnlyList<string> candidates)
    {
        _candidates = candidates;
        _resolveExecutable = resolveExecutable ?? (() => ExecutableResolver.FindOnPath(_candidates));
    }

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        string executable = _resolveExecutable() ?? throw new FileNotFoundException(
            $"PowerShell executable was not found. Tried: {string.Join(", ", _candidates)}");
        var startInfo = ShellProcessStartInfoFactory.Create(executable, workingDirectory);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

}

internal static class PowerShellExecutableCandidates
{
    public static IReadOnlyList<string> ForCurrentPlatform =>
        OperatingSystem.IsWindows() ? ["pwsh.exe", "powershell.exe"] : ["pwsh"];
}

internal static class ExecutableResolver
{
    public static string? FindOnPath(IEnumerable<string> candidates)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        string[] directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string executable in candidates)
        {
            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory.Trim(), executable);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
