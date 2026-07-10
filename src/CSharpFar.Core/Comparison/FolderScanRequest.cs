namespace CSharpFar.Core.Comparison;

public sealed record FolderScanRequest
{
    public required string RootPath { get; init; }
    public IReadOnlyList<string> SelectedPaths { get; init; } = [];
}
