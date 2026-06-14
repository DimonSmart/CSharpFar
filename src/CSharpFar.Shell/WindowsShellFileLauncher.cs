using System.Diagnostics;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Shell;

public sealed class WindowsShellFileLauncher : IFileLauncher
{
    private readonly IExecutableFileDetector _executableDetector;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Action<Process> _waitForExit;

    public WindowsShellFileLauncher()
        : this(new WindowsExecutableFileDetector())
    {
    }

    public WindowsShellFileLauncher(IExecutableFileDetector executableDetector)
        : this(executableDetector, Process.Start, process => process.WaitForExit())
    {
    }

    internal WindowsShellFileLauncher(
        Func<ProcessStartInfo, Process?> startProcess,
        Action<Process> waitForExit)
        : this(new WindowsExecutableFileDetector(), startProcess, waitForExit)
    {
    }

    internal WindowsShellFileLauncher(
        IExecutableFileDetector executableDetector,
        Func<ProcessStartInfo, Process?> startProcess,
        Action<Process> waitForExit)
    {
        _executableDetector = executableDetector;
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

        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = "open",
        };

        using var process = _startProcess(startInfo);
    }

    private void RunInCurrentConsole(string fullPath, string workingDirectory)
    {
        var startInfo = IsCommandScript(fullPath)
            ? CreateCommandScriptStartInfo(fullPath, workingDirectory)
            : CreateExecutableStartInfo(fullPath, workingDirectory);

        using var process = _startProcess(startInfo)
            ?? throw new InvalidOperationException($"Failed to start file: {fullPath}");

        _waitForExit(process);
    }

    private static ProcessStartInfo CreateExecutableStartInfo(
        string fullPath,
        string workingDirectory) =>
        CreateCurrentConsoleStartInfo(fullPath, workingDirectory);

    private static ProcessStartInfo CreateCommandScriptStartInfo(
        string fullPath,
        string workingDirectory)
    {
        var startInfo = CreateCurrentConsoleStartInfo(
            GetCommandProcessor(),
            workingDirectory);
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(fullPath);
        return startInfo;
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

    private static bool IsCommandScript(string fullPath)
    {
        string extension = Path.GetExtension(fullPath);
        return extension.Equals(".BAT", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".CMD", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCommandProcessor()
    {
        string? comspec = Environment.GetEnvironmentVariable("COMSPEC");
        return string.IsNullOrWhiteSpace(comspec) ? "cmd.exe" : comspec;
    }
}
