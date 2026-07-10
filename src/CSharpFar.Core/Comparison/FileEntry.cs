namespace CSharpFar.Core.Comparison;

public sealed record FileEntry
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public long? Size { get; init; }
    public DateTime? LastWriteTimeUtc { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public string? Error { get; init; }
    public string? ContentHash { get; init; }
}
