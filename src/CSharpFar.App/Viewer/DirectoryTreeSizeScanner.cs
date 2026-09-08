using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal enum DirectoryTreeSizeCompletionStatus
{
    Completed,
    CompletedWithErrors,
    Failed,
}

internal readonly record struct DirectoryTreeSizeProgress(
    long Size,
    IReadOnlyList<string> Errors);

internal sealed record DirectoryTreeSizeScanResult(
    long Size,
    DirectoryTreeSizeCompletionStatus Status,
    IReadOnlyList<string> Errors);

internal interface IDirectoryTreeSizeScanner
{
    DirectoryTreeSizeScanResult Scan(
        IFilePanelSource source,
        string rootSourcePath,
        Action<DirectoryTreeSizeProgress>? progress,
        CancellationToken cancellationToken);
}

internal static class DirectoryTraversalPolicy
{
    public static bool IsLinkLike(FilePanelItem item) =>
        (item.Attributes & FileAttributes.ReparsePoint) != 0;

    public static bool IsEligibleDirectory(FilePanelItem item) =>
        item.IsDirectory &&
        !item.IsParentDirectory &&
        !item.IsVolumeMountPoint &&
        !IsLinkLike(item);

    public static bool IsTraversableDirectory(FilePanelItem item) =>
        IsEligibleDirectory(item);

    public static bool IsCountableFile(FilePanelItem item) =>
        !item.IsDirectory &&
        !item.IsParentDirectory &&
        !IsLinkLike(item);
}

/// <summary>
/// Provider-independent recursive directory traversal primitive.
/// The scanner owns no operation lifetime: callers own cancellation, scheduling and stale-result protection.
/// </summary>
internal sealed class DirectoryTreeSizeScanner : IDirectoryTreeSizeScanner
{
    public const int DefaultThrottleMs = 300;

    private readonly int _throttleMs;

    public DirectoryTreeSizeScanner(int throttleMs = DefaultThrottleMs)
    {
        _throttleMs = Math.Max(0, throttleMs);
    }

    public DirectoryTreeSizeScanResult Scan(
        IFilePanelSource source,
        string rootSourcePath,
        Action<DirectoryTreeSizeProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootSourcePath);

        long total = 0;
        var errors = new List<string>();
        var stack = new Stack<(string Path, bool IsRoot)>();
        var visited = new HashSet<string>(PathComparer(source.SourceId));
        stack.Push((rootSourcePath, true));
        long lastProgressTick = Environment.TickCount64;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (path, isRoot) = stack.Pop();

            string normalizedPath;
            try
            {
                normalizedPath = source.NormalizePath(path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (isRoot)
                    return Failed(total, path, ex);

                errors.Add(FormatError(path, ex));
                ReportProgressIfDue(force: false);
                continue;
            }

            if (!visited.Add(normalizedPath))
            {
                if (!isRoot)
                    errors.Add($"{normalizedPath}: directory cycle or duplicate path skipped.");
                ReportProgressIfDue(force: false);
                continue;
            }

            IReadOnlyList<FilePanelItem> children;
            try
            {
                // EnumerateDirectory is synchronous. The caller is responsible for invoking
                // the whole Scan method from a background worker.
                children = source.EnumerateDirectory(normalizedPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (isRoot)
                    return Failed(total, normalizedPath, ex);

                errors.Add(FormatError(normalizedPath, ex));
                ReportProgressIfDue(force: false);
                continue;
            }

            foreach (FilePanelItem child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (child.IsParentDirectory || DirectoryTraversalPolicy.IsLinkLike(child))
                {
                    ReportProgressIfDue(force: false);
                    continue;
                }

                if (child.IsDirectory)
                {
                    if (!child.IsVolumeMountPoint)
                        stack.Push((child.SourcePath, false));

                    ReportProgressIfDue(force: false);
                    continue;
                }

                if (!DirectoryTraversalPolicy.IsCountableFile(child))
                {
                    ReportProgressIfDue(force: false);
                    continue;
                }

                if (child.Size is not { } size || size < 0)
                {
                    errors.Add($"{child.SourcePath}: file size is unavailable.");
                }
                else
                {
                    try
                    {
                        total = checked(total + size);
                    }
                    catch (OverflowException ex)
                    {
                        errors.Add(FormatError(child.SourcePath, ex));
                        total = long.MaxValue;
                    }
                }

                ReportProgressIfDue(force: false);
            }
        }

        return new DirectoryTreeSizeScanResult(
            total,
            errors.Count == 0
                ? DirectoryTreeSizeCompletionStatus.Completed
                : DirectoryTreeSizeCompletionStatus.CompletedWithErrors,
            [.. errors]);

        DirectoryTreeSizeScanResult Failed(long partial, string path, Exception exception)
        {
            errors.Add(FormatError(path, exception));
            return new DirectoryTreeSizeScanResult(
                partial,
                DirectoryTreeSizeCompletionStatus.Failed,
                [.. errors]);
        }

        void ReportProgressIfDue(bool force)
        {
            if (progress is null)
                return;

            long now = Environment.TickCount64;
            if (!force && _throttleMs > 0 && now - lastProgressTick < _throttleMs)
                return;

            lastProgressTick = now;
            progress(new DirectoryTreeSizeProgress(total, [.. errors]));
        }
    }

    private static StringComparer PathComparer(PanelSourceId sourceId) =>
        sourceId == PanelSourceId.Local
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static string FormatError(string path, Exception exception) =>
        $"{path}: {exception.Message}";
}
