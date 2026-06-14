using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellService : IShellService
{
    private readonly IShellCommandLineBuilder _commandLineBuilder;

    public ShellService()
        : this(new WindowsShellCommandLineBuilder("cmd.exe"))
    {
    }

    public ShellService(IShellCommandLineBuilder commandLineBuilder)
    {
        _commandLineBuilder = commandLineBuilder;
    }

    public ShellService(string shellExecutable, string shellArgsFormat)
        : this(CreateCompatibilityBuilder(shellExecutable, shellArgsFormat))
    {
    }

    public void Execute(string command, string workingDirectory)
    {
        using var process = Process.Start(_commandLineBuilder.CreateStartInfo(command, workingDirectory))
            ?? throw new InvalidOperationException("Failed to start shell process.");

        process.WaitForExit();
    }

    private static IShellCommandLineBuilder CreateCompatibilityBuilder(
        string shellExecutable,
        string shellArgsFormat)
    {
        if (shellArgsFormat.TrimStart().StartsWith("-c", StringComparison.Ordinal))
            return new UnixShellCommandLineBuilder(shellExecutable);

        return new WindowsShellCommandLineBuilder(shellExecutable);
    }
}
