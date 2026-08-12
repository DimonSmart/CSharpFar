namespace CSharpFar.Core.Abstractions;

public interface IShellService
{
    /// <summary>
    /// Executes a shell command in the given working directory.
    /// The command's output appears directly in the console.
    /// Blocks until the command exits.
    /// </summary>
    void Execute(string command, string workingDirectory);

    bool TryParseInvocation(string command, out ShellInvocation invocation)
    {
        invocation = default!;
        return false;
    }

    void Execute(ShellInvocation invocation, string workingDirectory) =>
        Execute(invocation.Command, workingDirectory);
}
