namespace CSharpFar.Core.Models;

public sealed class VolumeSpaceInfo
{
    public required string Path { get; init; }
    public required long FreeBytesAvailable { get; init; }
    public required long TotalBytes { get; init; }
    public string? VolumeLabel { get; init; }
}
