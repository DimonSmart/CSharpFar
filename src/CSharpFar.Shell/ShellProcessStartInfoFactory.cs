using System.Diagnostics;

namespace CSharpFar.Shell;

internal static class ShellProcessStartInfoFactory
{
    public static ProcessStartInfo Create(string fileName, string workingDirectory) => new()
    {
        FileName = fileName,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false,
        CreateNoWindow = false,
    };

    public static void ApplyPath(ProcessStartInfo startInfo, string? effectivePath)
    {
        if (effectivePath is not null)
            startInfo.Environment["PATH"] = effectivePath;
    }
}
