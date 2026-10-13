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
    private readonly Func<string?>? _effectivePathProvider;

    public UnixShellFileLauncher(IExecutableFileDetector executableDetector)
        : this(executableDetector, new UnixAssociationLauncher(new UnixEnvironment()))
    {
    }

    public UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher)
        : this(executableDetector, associationLauncher, Process.Start, process => process.WaitForExit(), null)
    {
    }

    public UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher,
        Func<string?> effectivePathProvider)
        : this(executableDetector, associationLauncher, Process.Start,
            process => process.WaitForExit(), effectivePathProvider)
    {
    }

    internal UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher,
        Func<ProcessStartInfo, Process?> startProcess,
        Action<Process> waitForExit)
        : this(executableDetector, associationLauncher, startProcess, waitForExit, null)
    {
    }

    internal UnixShellFileLauncher(
        IExecutableFileDetector executableDetector,
        IUnixAssociationLauncher associationLauncher,
        Func<ProcessStartInfo, Process?> startProcess,
        Action<Process> waitForExit,
        Func<string?>? effectivePathProvider)
    {
        _executableDetector = executableDetector;
        _associationLauncher = associationLauncher;
        _startProcess = startProcess;
        _waitForExit = waitForExit;
        _effectivePathProvider = effectivePathProvider;
    }

    public FileLaunchMode GetLaunchMode(string fullPath) =>
        _executableDetector.IsExecutableFile(fullPath)
            ? FileLaunchMode.CurrentConsole
            : FileLaunchMode.AssociatedDetached;

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

    private ProcessStartInfo CreateCurrentConsoleStartInfo(string fileName, string workingDirectory)
    {
        var startInfo = ShellProcessStartInfoFactory.Create(fileName, workingDirectory);
        ShellProcessStartInfoFactory.ApplyPath(startInfo, _effectivePathProvider?.Invoke());
        return startInfo;
    }
}
