namespace CSharpFar.Core.Models;

public sealed record FileOperationOptions
{
    public ConflictDecisionMode DefaultConflictDecision { get; init; } = ConflictDecisionMode.Ask;
    public bool OnlyNewer { get; init; }
    public bool PreserveTimestamps { get; init; } = true;
    public bool PreserveAttributes { get; init; } = true;
    public FileSecurityMode SecurityMode { get; init; } = FileSecurityMode.Inherit;
    public SymlinkCopyMode SymlinkMode { get; init; } = SymlinkCopyMode.CopyLink;
    public bool UseRecycleBinForDelete { get; init; }
    public string? FileMask { get; init; }
}
