namespace CSharpFar.Core.Models;

public sealed record SearchProgress
{
    public long ScannedDirectories { get; init; }
    public long ScannedFiles { get; init; }
    public long MatchedItems { get; init; }
    public long ErrorCount { get; init; }
    public string? CurrentPath { get; init; }
    public string? LastErrorPath { get; init; }
    public string? LastErrorMessage { get; init; }
}
