namespace CSharpFar.Core.Models;

public sealed class FileSystemVolume
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string RootPath { get; init; }
    public VolumeKind Kind { get; init; }
    public VolumeStatus Status { get; init; }
    public long? TotalBytes { get; init; }
    public long? FreeBytes { get; init; }
    public string? Shortcut { get; init; }
}
