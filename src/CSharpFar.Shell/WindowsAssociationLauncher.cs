using System.Diagnostics;

namespace CSharpFar.Shell;

internal sealed class WindowsAssociationLauncher : IWindowsAssociationLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public WindowsAssociationLauncher()
        : this(Process.Start)
    {
    }

    internal WindowsAssociationLauncher(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    public void OpenDetached(WindowsAssociationLaunchRequest request)
    {
        if (!request.Verb.Equals("open", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported file association verb: {request.Verb}");

        using var process = _startProcess(CreateStartInfo(request))
            ?? throw new InvalidOperationException($"Failed to open file: {request.FullPath}");
    }

    private static ProcessStartInfo CreateStartInfo(WindowsAssociationLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(request.FullPath);
        return startInfo;
    }
}
