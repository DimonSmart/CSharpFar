namespace CSharpFar.Core.Services;

/// <summary>Provides path equality consistent with the host local filesystem.</summary>
public static class LocalFileSystemPathComparer
{
    public static StringComparer Current { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
