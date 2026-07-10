using System.Diagnostics;

namespace CSharpFar.Core.Comparison;

public sealed class FolderStructureCompareEngine
{
    private readonly FolderScanner _scanner;
    private readonly IComparisonFileSystem _fileSystem;

    public FolderStructureCompareEngine(
        FolderScanner? scanner = null,
        IComparisonFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new LocalComparisonFileSystem();
        _scanner = scanner ?? new FolderScanner(_fileSystem);
    }

    public CompareResult Compare(
        FolderScanRequest left,
        FolderScanRequest right,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var effectiveOptions = options with { Mode = CompareMode.FolderStructure };
        var leftEntries = _scanner.Scan(left, effectiveOptions, cancellationToken);
        var rightEntries = _scanner.Scan(right, effectiveOptions, cancellationToken);
        var comparer = CreateComparer(effectiveOptions);
        var keyComparer = effectiveOptions.IsNameComparisonCaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        var leftByKey = leftEntries.GroupBy(entry => entry.RelativePath, keyComparer).ToDictionary(g => g.Key, g => g.ToList(), keyComparer);
        var rightByKey = rightEntries.GroupBy(entry => entry.RelativePath, keyComparer).ToDictionary(g => g.Key, g => g.ToList(), keyComparer);
        var keys = leftByKey.Keys.Concat(rightByKey.Keys).Distinct(keyComparer).OrderBy(key => key, keyComparer);
        var rows = new List<CompareResultRow>();
        long comparedBytes = 0;

        foreach (string key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            leftByKey.TryGetValue(key, out var leftGroup);
            rightByKey.TryGetValue(key, out var rightGroup);
            leftGroup ??= [];
            rightGroup ??= [];

            var error = leftGroup.Concat(rightGroup).FirstOrDefault(entry => entry.Error is not null);
            if (error is not null)
            {
                rows.Add(Row(CompareStatus.Error, key, leftGroup, rightGroup, error.Error));
                continue;
            }

            if (leftGroup.Count == 0)
            {
                rows.Add(Row(CompareStatus.RightOnly, key, [], rightGroup));
                continue;
            }

            if (rightGroup.Count == 0)
            {
                rows.Add(Row(CompareStatus.LeftOnly, key, leftGroup, []));
                continue;
            }

            if (leftGroup.Count != 1 || rightGroup.Count != 1)
            {
                rows.Add(Row(CompareStatus.Error, key, leftGroup, rightGroup, "Relative path produced multiple files."));
                continue;
            }

            try
            {
                var outcome = comparer.Compare(leftGroup[0], rightGroup[0], cancellationToken);
                comparedBytes += outcome.ComparedBytes;
                rows.Add(Row(outcome.Equal ? CompareStatus.Equal : CompareStatus.Different, key, leftGroup, rightGroup, outcome.Message));
            }
            catch (Exception ex) when (FolderScanner.IsFileSystemException(ex))
            {
                rows.Add(Row(CompareStatus.Error, key, leftGroup, rightGroup, FolderScanner.ShortMessage(ex)));
            }
        }

        watch.Stop();
        return BuildResult(CompareMode.FolderStructure, rows, leftEntries, rightEntries, watch.Elapsed, comparedBytes);
    }

    private IFileComparer CreateComparer(ComparisonOptions options) =>
        options.Method switch
        {
            CompareMethod.Content => new ByteContentFileComparer(_fileSystem),
            CompareMethod.Text => new ByteContentFileComparer(_fileSystem),
            _ => new FastFileComparer(options.TimestampToleranceValue),
        };

    internal static CompareResult BuildResult(
        CompareMode mode,
        IReadOnlyList<CompareResultRow> rows,
        IReadOnlyList<FileEntry> leftEntries,
        IReadOnlyList<FileEntry> rightEntries,
        TimeSpan duration,
        long comparedBytes) =>
        new()
        {
            Mode = mode,
            Rows = rows,
            Summary = new CompareSummary
            {
                TotalFilesLeft = leftEntries.Count(entry => !entry.IsDirectory && entry.Error is null),
                TotalFilesRight = rightEntries.Count(entry => !entry.IsDirectory && entry.Error is null),
                EqualCount = rows.Count(row => row.Status == CompareStatus.Equal),
                DifferentCount = rows.Count(row => row.Status == CompareStatus.Different),
                LeftOnlyCount = rows.Count(row => row.Status == CompareStatus.LeftOnly),
                RightOnlyCount = rows.Count(row => row.Status == CompareStatus.RightOnly),
                AmbiguousCount = rows.Count(row => row.Status is CompareStatus.Ambiguous or CompareStatus.Duplicate),
                ErrorCount = rows.Count(row => row.Status == CompareStatus.Error),
                Duration = duration,
                ComparedBytes = comparedBytes,
            },
        };

    internal static CompareResultRow Row(
        CompareStatus status,
        string key,
        IReadOnlyList<FileEntry> left,
        IReadOnlyList<FileEntry> right,
        string? message = null) =>
        new()
        {
            Status = status,
            Key = key,
            LeftEntries = left,
            RightEntries = right,
            Message = message,
        };
}
