namespace CSharpFar.Core.Models;

public sealed record SearchRequest
{
    public required string RootPath { get; init; }
    public required string FileMaskExpression { get; init; }
    public string? ContainingText { get; init; }
    public bool CaseSensitive { get; init; }
    public bool WholeWords { get; init; }
    public bool NotContaining { get; init; }
    public bool IncludeDirectoriesInResults { get; init; }
    public bool SearchInSymbolicLinks { get; init; }
    public SearchScope Scope { get; init; }
    public int MaxDegreeOfParallelism { get; init; }
    public SearchEncodingMode EncodingMode { get; init; } = SearchEncodingMode.Automatic;
    public long MaxContentSearchFileSizeBytes { get; init; } = 100L * 1024 * 1024;
}
