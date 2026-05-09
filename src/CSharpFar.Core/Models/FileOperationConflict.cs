namespace CSharpFar.Core.Models;

public sealed record FileOperationConflict
{
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public bool SourceIsDirectory { get; init; }
    public bool DestinationIsDirectory { get; init; }
    public long? SourceSize { get; init; }
    public long? DestinationSize { get; init; }
    public DateTime? SourceLastWriteTime { get; init; }
    public DateTime? DestinationLastWriteTime { get; init; }
}
