namespace CSharpFar.FileSystem;

public sealed record LinuxMountInfoEntry(
    string Source,
    string MountPoint,
    string FileSystemType);
