namespace CSharpFar.Core.Models;

public sealed record FileOperationResult
{
    public required FileOperationKind Kind { get; init; }
    public int CopiedCount { get; init; }
    public int MovedCount { get; init; }
    public int DeletedCount { get; init; }
    public int CreatedDirectoryCount { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount => Errors.Count;
    public bool Cancelled { get; init; }
    public long BytesProcessed { get; init; }
    public long TotalBytes { get; init; }
    public required IReadOnlyList<FileOperationItemError> Errors { get; init; }
}
