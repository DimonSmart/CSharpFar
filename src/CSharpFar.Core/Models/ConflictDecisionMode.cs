namespace CSharpFar.Core.Models;

public enum ConflictDecisionMode
{
    Ask,
    Overwrite,
    OverwriteAll,
    Skip,
    SkipAll,
    Rename,
    RenameAll,
    Merge,
    Replace,
    OnlyNewer,
    Cancel,
}
