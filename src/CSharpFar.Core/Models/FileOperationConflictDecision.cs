namespace CSharpFar.Core.Models;

public sealed record FileOperationConflictDecision
{
    public required ConflictDecisionMode Mode { get; init; }
    public string? NewDestinationPath { get; init; }

    public static FileOperationConflictDecision FromMode(ConflictDecisionMode mode) =>
        new() { Mode = mode };
}
