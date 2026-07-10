namespace CSharpFar.Core.Comparison;

public sealed record CompareResultRow
{
    public required CompareStatus Status { get; init; }
    public required string Key { get; init; }
    public IReadOnlyList<FileEntry> LeftEntries { get; init; } = [];
    public IReadOnlyList<FileEntry> RightEntries { get; init; } = [];
    public string? LeftPath => LeftEntries.Count == 1 ? LeftEntries[0].FullPath : null;
    public string? RightPath => RightEntries.Count == 1 ? RightEntries[0].FullPath : null;
    public long? LeftSize => LeftEntries.Count == 1 ? LeftEntries[0].Size : null;
    public long? RightSize => RightEntries.Count == 1 ? RightEntries[0].Size : null;
    public DateTime? LeftLastWriteTimeUtc => LeftEntries.Count == 1 ? LeftEntries[0].LastWriteTimeUtc : null;
    public DateTime? RightLastWriteTimeUtc => RightEntries.Count == 1 ? RightEntries[0].LastWriteTimeUtc : null;
    public string? Message { get; init; }
}

public sealed record CompareSummary
{
    public int TotalFilesLeft { get; init; }
    public int TotalFilesRight { get; init; }
    public int EqualCount { get; init; }
    public int DifferentCount { get; init; }
    public int LeftOnlyCount { get; init; }
    public int RightOnlyCount { get; init; }
    public int AmbiguousCount { get; init; }
    public int ErrorCount { get; init; }
    public TimeSpan Duration { get; init; }
    public long ComparedBytes { get; init; }
}

public sealed record CompareResult
{
    public required CompareMode Mode { get; init; }
    public required IReadOnlyList<CompareResultRow> Rows { get; init; }
    public required CompareSummary Summary { get; init; }
}
