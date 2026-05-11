namespace CSharpFar.Core.Models;

public sealed record FileOperationProgress
{
    public required FileOperationKind Kind { get; init; }
    public FileOperationPhase Phase { get; init; } = FileOperationPhase.Copying;
    public required string CurrentPath { get; init; }
    public string? CurrentDestinationPath { get; init; }
    public string? StatusMessage { get; init; }
    public long CurrentBytesDone { get; init; }
    public long CurrentBytesTotal { get; init; }
    public long TotalBytesDone { get; init; }
    public long TotalBytesTotal { get; init; }
    public long? ResumeOffset { get; init; }
    public long? ResumeRollbackBytes { get; init; }
    public int ItemsDone { get; init; }
    public int ItemsTotal { get; init; }
    public int FoldersDone { get; init; }
    public double BytesPerSecond { get; init; }
    public TimeSpan? TimeRemaining { get; init; }
    public TimeSpan Elapsed { get; init; }
}
