using System.Diagnostics;

namespace CSharpFar.Core.Comparison;

public sealed class FileSetCompareEngine
{
    private readonly FolderScanner _scanner;
    private readonly IComparisonFileSystem _fileSystem;
    private readonly FileContentHasher _hasher;

    public FileSetCompareEngine(
        FolderScanner? scanner = null,
        IComparisonFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new LocalComparisonFileSystem();
        _scanner = scanner ?? new FolderScanner(_fileSystem);
        _hasher = new FileContentHasher(_fileSystem);
    }

    public CompareResult Compare(
        FolderScanRequest left,
        FolderScanRequest right,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        var effectiveOptions = options with { Mode = CompareMode.FileSet };
        var leftEntries = Prepare(_scanner.Scan(left, effectiveOptions, cancellationToken), effectiveOptions, cancellationToken);
        var rightEntries = Prepare(_scanner.Scan(right, effectiveOptions, cancellationToken), effectiveOptions, cancellationToken);
        IFileComparer comparer = effectiveOptions.Method == CompareMethod.Content
            ? new ByteContentFileComparer(_fileSystem)
            : new FastFileComparer(effectiveOptions.TimestampToleranceValue);
        var keyComparer = effectiveOptions.IsNameComparisonCaseSensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var leftGroups = leftEntries.GroupBy(entry => FileSetKey(entry, effectiveOptions), keyComparer).ToDictionary(g => g.Key, g => g.ToList(), keyComparer);
        var rightGroups = rightEntries.GroupBy(entry => FileSetKey(entry, effectiveOptions), keyComparer).ToDictionary(g => g.Key, g => g.ToList(), keyComparer);
        var keys = leftGroups.Keys.Concat(rightGroups.Keys).Distinct(keyComparer).OrderBy(key => key, keyComparer);
        var rows = new List<CompareResultRow>();
        long comparedBytes = 0;

        foreach (string key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            leftGroups.TryGetValue(key, out var leftGroup);
            rightGroups.TryGetValue(key, out var rightGroup);
            leftGroup ??= [];
            rightGroup ??= [];

            var error = leftGroup.Concat(rightGroup).FirstOrDefault(entry => entry.Error is not null);
            if (error is not null)
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.Error, key, leftGroup, rightGroup, error.Error));
                continue;
            }

            if (leftGroup.Count == 0)
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.RightOnly, key, [], rightGroup));
                continue;
            }

            if (rightGroup.Count == 0)
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.LeftOnly, key, leftGroup, []));
                continue;
            }

            if (leftGroup.Count != 1 || rightGroup.Count != 1)
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.Ambiguous, key, leftGroup, rightGroup, "Multiple files share this match key."));
                continue;
            }

            if (effectiveOptions.FileSetMatchMode == FileSetMatchMode.FileNameAndContentHash)
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.Equal, key, leftGroup, rightGroup));
                continue;
            }

            try
            {
                var outcome = comparer.Compare(leftGroup[0], rightGroup[0], cancellationToken);
                comparedBytes += outcome.ComparedBytes;
                rows.Add(FolderStructureCompareEngine.Row(outcome.Equal ? CompareStatus.Equal : CompareStatus.Different, key, leftGroup, rightGroup, outcome.Message));
            }
            catch (Exception ex) when (FolderScanner.IsFileSystemException(ex))
            {
                rows.Add(FolderStructureCompareEngine.Row(CompareStatus.Error, key, leftGroup, rightGroup, FolderScanner.ShortMessage(ex)));
            }
        }

        watch.Stop();
        return FolderStructureCompareEngine.BuildResult(CompareMode.FileSet, rows, leftEntries, rightEntries, watch.Elapsed, comparedBytes);
    }

    private IReadOnlyList<FileEntry> Prepare(
        IReadOnlyList<FileEntry> entries,
        ComparisonOptions options,
        CancellationToken cancellationToken)
    {
        if (options.FileSetMatchMode != FileSetMatchMode.FileNameAndContentHash)
            return entries;

        return entries.Select(entry =>
        {
            if (entry.Error is not null)
                return entry;

            try
            {
                return entry with { ContentHash = _hasher.ComputeSha256(entry, cancellationToken) };
            }
            catch (Exception ex) when (FolderScanner.IsFileSystemException(ex))
            {
                return entry with { Error = FolderScanner.ShortMessage(ex) };
            }
        }).ToList();
    }

    private static string FileSetKey(FileEntry entry, ComparisonOptions options) =>
        options.FileSetMatchMode switch
        {
            FileSetMatchMode.FileNameAndSize => $"{entry.FileName}|{entry.Size?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}",
            FileSetMatchMode.FileNameAndContentHash => $"{entry.FileName}|{entry.ContentHash ?? "error"}",
            _ => entry.FileName,
        };
}
