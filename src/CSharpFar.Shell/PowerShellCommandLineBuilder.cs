using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class PowerShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly Func<string?>? _resolveExecutable;
    private readonly Func<string?>? _effectivePathProvider;
    private readonly IReadOnlyList<string> _candidates;

    public PowerShellCommandLineBuilder(Func<string?>? resolveExecutable = null)
        : this(resolveExecutable, PowerShellExecutableCandidates.ForCurrentPlatform, null)
    {
    }

    public PowerShellCommandLineBuilder(Func<string?>? resolveExecutable, Func<string?> effectivePathProvider)
        : this(resolveExecutable, PowerShellExecutableCandidates.ForCurrentPlatform, effectivePathProvider)
    {
    }

    internal PowerShellCommandLineBuilder(Func<string?>? resolveExecutable, IReadOnlyList<string> candidates)
        : this(resolveExecutable, candidates, null)
    {
    }

    internal PowerShellCommandLineBuilder(
        Func<string?>? resolveExecutable,
        IReadOnlyList<string> candidates,
        Func<string?>? effectivePathProvider)
    {
        _candidates = candidates;
        _resolveExecutable = resolveExecutable;
        _effectivePathProvider = effectivePathProvider;
    }

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        string? effectivePath = _effectivePathProvider?.Invoke();
        string? executable = _resolveExecutable is not null
            ? _resolveExecutable()
            : (effectivePath is null
                ? ExecutableResolver.FindOnPath(_candidates)
                : ExecutableResolver.FindOnPath(_candidates, effectivePath, ':'));
        executable ??= throw new FileNotFoundException(
            $"PowerShell executable was not found. Tried: {string.Join(", ", _candidates)}");

        var startInfo = ShellProcessStartInfoFactory.Create(executable, workingDirectory);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        ShellProcessStartInfoFactory.ApplyPath(startInfo, effectivePath);
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
    // Preserve the legacy search policy for Windows and Linux.
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

    // The macOS search uses the same effective PATH as the child's environment.
    public static string? FindOnPath(IEnumerable<string> candidates, string path, char separator)
    {
        foreach (string directory in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string executable in candidates)
            {
                string fileName = Path.Combine(directory, executable);
                if (!File.Exists(fileName))
                    continue;

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        UnixFileMode mode = File.GetUnixFileMode(fileName);
                        if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                                     UnixFileMode.OtherExecute)) == 0)
                            continue;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                               PlatformNotSupportedException)
                    {
                        continue;
                    }
                }

                return Path.GetFullPath(fileName);
            }
        }

        return null;
    }
}
