using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class SystemUriLauncher : IUriLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public SystemUriLauncher()
        : this(Process.Start)
    {
    }

    internal SystemUriLauncher(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));

        var startInfo = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        };

        using Process? process = _startProcess(startInfo);
        if (process is null)
            throw new InvalidOperationException($"Failed to open URI: {uri}");
    }
}
