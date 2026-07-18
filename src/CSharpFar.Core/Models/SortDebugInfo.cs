namespace CSharpFar.Core.Models;

public sealed class SortDebugInfo
{
    public required string PrimaryKey { get; init; }
    public required string SecondaryKey { get; init; }
    public required bool IsDirectory { get; init; }
    public required bool IsParentDirectory { get; init; }
}
