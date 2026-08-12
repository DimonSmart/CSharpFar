using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellService : IShellService
{
    private readonly IShellCommandLineBuilder _commandLineBuilder;
    private readonly ShellRegistry _registry;

    public ShellService()
        : this(new WindowsShellCommandLineBuilder("cmd.exe"), new ShellRegistry())
    {
    }

    public ShellService(IShellCommandLineBuilder commandLineBuilder)
        : this(commandLineBuilder, new ShellRegistry())
    {
    }

    public ShellService(IShellCommandLineBuilder commandLineBuilder, ShellRegistry registry)
    {
        _commandLineBuilder = commandLineBuilder;
        _registry = registry;
    }

    public ShellService(string shellExecutable, string shellArgsFormat)
        : this(CreateCompatibilityBuilder(shellExecutable, shellArgsFormat))
    {
    }

    public void Execute(string command, string workingDirectory)
    {
        Execute(_commandLineBuilder, command, workingDirectory);
    }

    public void Execute(ShellInvocation invocation, string workingDirectory)
    {
        if (!_registry.TryGetById(invocation.ShellId, out ShellProfile profile))
            throw new InvalidOperationException($"Shell '{invocation.ShellId}' is not registered.");
        try { Execute(profile.CommandLineBuilder, invocation.Command, workingDirectory); }
        catch (FileNotFoundException ex) { Console.Error.WriteLine(ex.Message); }
    }

    private static void Execute(IShellCommandLineBuilder builder, string command, string workingDirectory)
    {
        using var process = Process.Start(builder.CreateStartInfo(command, workingDirectory))
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
