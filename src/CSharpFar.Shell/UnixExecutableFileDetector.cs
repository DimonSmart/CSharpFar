using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class UnixExecutableFileDetector : IExecutableFileDetector
{
    public bool IsExecutableFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return false;

        if (!File.Exists(path))
            return false;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
                return false;

            var mode = File.GetUnixFileMode(path);
            return
                (mode & UnixFileMode.UserExecute) != 0 ||
                (mode & UnixFileMode.GroupExecute) != 0 ||
                (mode & UnixFileMode.OtherExecute) != 0;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
