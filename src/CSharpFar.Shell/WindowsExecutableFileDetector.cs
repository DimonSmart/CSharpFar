using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class WindowsExecutableFileDetector : IExecutableFileDetector
{
    private static readonly string[] FallbackExtensions = [".exe", ".com", ".bat", ".cmd"];
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public WindowsExecutableFileDetector()
        : this(File.Exists, Environment.GetEnvironmentVariable)
    {
    }

    internal WindowsExecutableFileDetector(
        Func<string, bool> fileExists,
        Func<string, string?> getEnvironmentVariable)
    {
        _fileExists = fileExists;
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    public bool IsExecutableFile(string path)
    {
        if (!_fileExists(path))
            return false;

        string extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        string? pathext = _getEnvironmentVariable("PATHEXT");
        string[] extensions = string.IsNullOrWhiteSpace(pathext)
            ? FallbackExtensions
            : pathext.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
