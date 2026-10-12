namespace CSharpFar.FileSystem;

/// <summary>A cached, non-blocking native mount snapshot; independent of the drive dialog.</summary>
public interface IMacOsMountProvider
{
    IReadOnlyList<MacOsMount> GetMounts();
}

/// <summary>Native mount identity is its exact mount path, never its display name.</summary>
public sealed record MacOsMount(
    string RootPath,
    string FileSystemType,
    bool IsHidden,
    long? TotalBytes = null,
    long? FreeBytes = null,
    string? DisplayName = null);
