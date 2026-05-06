using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellService : IShellService
{
    private readonly string _shellExecutable;
    private readonly string _shellArgsFormat; // e.g. "/c {0}"

    public ShellService(string shellExecutable = "cmd.exe", string shellArgsFormat = "/c {0}")
    {
        _shellExecutable = shellExecutable;
        _shellArgsFormat = shellArgsFormat;
    }

    public void Execute(string command, string workingDirectory)
    {
        string args = string.Format(_shellArgsFormat, command);

        var psi = new ProcessStartInfo
        {
            FileName         = _shellExecutable,
            Arguments        = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute  = false,
            // stdin/stdout/stderr are NOT redirected → the child process inherits
            // our console and its output appears directly in the console buffer.
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start shell process: {_shellExecutable}");

        process.WaitForExit();
    }
}
