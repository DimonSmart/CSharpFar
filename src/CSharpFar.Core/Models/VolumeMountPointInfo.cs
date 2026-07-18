namespace CSharpFar.Core.Models;

public sealed class VolumeMountPointInfo
{
    public required bool IsVolumeMountPoint { get; init; }
    public string? VolumeName { get; init; }
    public string? VolumePath { get; init; }
}
