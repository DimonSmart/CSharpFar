using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class UnixShellCommandLineBuilder : IShellCommandLineBuilder
{
    private readonly string _shellExecutable;
    private readonly string[] _options;
    private readonly Func<string?>? _effectivePathProvider;

    public UnixShellCommandLineBuilder(string shellExecutable = "/bin/sh")
        : this(shellExecutable, "-c")
    {
    }

    public UnixShellCommandLineBuilder(
        string shellExecutable,
        string? argumentsFormat,
        Func<string?>? effectivePathProvider = null)
    {
        _shellExecutable = string.IsNullOrWhiteSpace(shellExecutable) ? "/bin/sh" : shellExecutable;
        _effectivePathProvider = effectivePathProvider;
        if (!UnixShellArguments.TryParse(argumentsFormat, out _options))
        {
            Console.Error.WriteLine($"Invalid shell.argumentsFormat '{argumentsFormat}'; using -c.");
            _options = ["-c"];
        }
    }

    public ProcessStartInfo CreateStartInfo(string command, string workingDirectory)
    {
        string? effectivePath = _effectivePathProvider?.Invoke();
        string executable = _shellExecutable;
        if (effectivePath is not null && !executable.Contains('/'))
            executable = ExecutableResolver.FindOnPath([executable], effectivePath, ':') ?? executable;

        var startInfo = ShellProcessStartInfoFactory.Create(executable, workingDirectory);
        foreach (string option in _options)
            startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(command);
        ShellProcessStartInfoFactory.ApplyPath(startInfo, effectivePath);
        return startInfo;
    }
}

internal static class UnixShellArguments
{
    public static bool TryParse(string? format, out string[] options)
    {
        options = ["-c"];
        if (string.IsNullOrWhiteSpace(format))
            return true;

        string[] tokens = format.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int count = tokens.Length;
        if (count > 0 && tokens[^1] == "{0}")
            count--;

        if (count == 0)
            return false;

        int commandFlags = 0;
        for (int index = 0; index < count; index++)
        {
            string token = tokens[index];
            if (token.Length < 2 || token[0] != '-')
                return false;

            for (int charIndex = 1; charIndex < token.Length; charIndex++)
            {
                char option = token[charIndex];
                if (!((option >= 'a' && option <= 'z') || (option >= 'A' && option <= 'Z')))
                    return false;

                if (option == 'c')
                {
                    commandFlags++;
                    if (index != count - 1 || charIndex != token.Length - 1)
                        return false;
                }
            }
        }

        if (commandFlags != 1)
            return false;

        options = tokens[..count];
        return true;
    }
}
