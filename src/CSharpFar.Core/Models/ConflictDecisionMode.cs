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
    Append,
    AppendAll,
    OnlyNewer,
    Cancel,
}
