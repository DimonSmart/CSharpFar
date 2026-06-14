namespace CSharpFar.FileSystem;

public sealed record UnixMountInfoEntry(
    string Source,
    string MountPoint,
    string FileSystemType);
