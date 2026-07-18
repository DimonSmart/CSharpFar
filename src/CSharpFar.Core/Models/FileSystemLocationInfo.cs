namespace CSharpFar.Core.Models;

public sealed class FileSystemLocationInfo
{
    public required string Path { get; init; }
    public required bool IsNetworkDrive { get; init; }
    public required bool IsRemovableDrive { get; init; }
    public required bool IsFixedDrive { get; init; }
    public required string? RootPath { get; init; }
}
