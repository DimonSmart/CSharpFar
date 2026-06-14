using System.Diagnostics;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Shell;

public sealed class UnixShellFileLauncher : IFileLauncher
{
    private readonly IExecutableFileDetector _executableDetector;
    private readonly IUnixAssociationLauncher _associationLauncher;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Action<Process> _waitForExit;

    public UnixShellFileLauncher(IExecutableFileDetector executableDetector)
        : this(executableDetector, new UnixAssociationLauncher(new UnixEnvironment()))
    {
    }

    public UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher)
        : this(executableDetector, associationLauncher, Process.Start, process => process.WaitForExit())
    {
    }

    internal UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher,
        Func<ProcessStartInfo, Process?> startProcess,
        Action<Process> waitForExit)
    {
        _executableDetector = executableDetector;
        _associationLauncher = associationLauncher;
        _startProcess = startProcess;
        _waitForExit = waitForExit;
    }

    public FileLaunchMode GetLaunchMode(string fullPath) =>
        _executableDetector.IsExecutableFile(fullPath)
            ? FileLaunchMode.CurrentConsole
            : FileLaunchMode.ShellAssociation;

    public void OpenFile(string fullPath, string workingDirectory)
    {
        if (GetLaunchMode(fullPath) == FileLaunchMode.CurrentConsole)
        {
            RunInCurrentConsole(fullPath, workingDirectory);
            return;
        }

        OpenAssociation(fullPath, workingDirectory);
    }

    private void RunInCurrentConsole(string fullPath, string workingDirectory)
    {
        using var process = _startProcess(CreateCurrentConsoleStartInfo(fullPath, workingDirectory))
            ?? throw new InvalidOperationException($"Failed to start file: {fullPath}");

        _waitForExit(process);
    }

    private void OpenAssociation(string fullPath, string workingDirectory)
    {
        if (!_associationLauncher.TryOpen(fullPath, workingDirectory, out string? error))
            throw new InvalidOperationException(error);
    }

    private static ProcessStartInfo CreateCurrentConsoleStartInfo(
        string fileName,
        string workingDirectory) =>
        new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false,
        };
}
