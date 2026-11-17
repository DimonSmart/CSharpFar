using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class PanelViewBuilder : IAsyncPanelViewBuilder
{
    private readonly IFileSystemService _fs;
    private readonly IPanelSortService _sort;
    private readonly IVolumeInfoService? _volumeInfo;
    private readonly IVolumeMountPointService? _mountPoints;
    private readonly IFilePanelSourceRegistry? _sources;
    private readonly IPanelPathSemantics _pathSemantics;

    public PanelViewBuilder(
        IFileSystemService fs,
        IPanelSortService sort,
        IVolumeInfoService? volumeInfo = null,
        IVolumeMountPointService? mountPoints = null,
        IFilePanelSourceRegistry? sources = null,
        IPanelPathSemantics? pathSemantics = null)
    {
        _fs = fs;
        _sort = sort;
        _volumeInfo = volumeInfo;
        _mountPoints = mountPoints;
        _sources = sources;
        _pathSemantics = pathSemantics ?? PanelPathSemantics.Current;
    }

    public PanelView Build(PanelViewRequest request) =>
        BuildCore(request, CancellationToken.None, preloadedEntries: null);

    public PanelView Build(PanelViewRequest request, CancellationToken cancellationToken) =>
        BuildCore(request, cancellationToken, preloadedEntries: null);

    /// <summary>
    /// Run synchronous providers on a worker; let providers implementing the optional
    /// async contract perform actual asynchronous I/O. No panel state is touched here.
    /// </summary>
    public Task<PanelView> BuildAsync(PanelViewRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = request.Location ?? PanelLocation.Local(request.DirectoryPath);
            if (location.SourceId == PanelSourceId.Local)
                return BuildCore(request, cancellationToken, preloadedEntries: null);

            var source = ResolveSource(location.SourceId);
            if (source is not IAsyncFilePanelSource asyncSource)
                return BuildCore(request, cancellationToken, preloadedEntries: null);

            string path = FromProvider(source, () => source.NormalizePath(location.SourcePath));
            IReadOnlyList<FilePanelItem> entries;
            try
            {
                entries = await asyncSource.EnumerateDirectoryAsync(path, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is not IOException
                and not UnauthorizedAccessException
                and not OperationCanceledException
                and not PanelSourceUnavailableException)
            {
                throw new IOException($"Panel provider '{source.SourceId}' failed: {error.Message}", error);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return BuildCore(request, cancellationToken, entries);
        }, cancellationToken);

    private PanelView BuildCore(
        PanelViewRequest request, CancellationToken cancellationToken,
        IReadOnlyList<FilePanelItem>? preloadedEntries)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opts = request.Options;
        var location = request.Location ?? PanelLocation.Local(request.DirectoryPath);
        bool isLocal = location.SourceId == PanelSourceId.Local;
        if (isLocal &&
            _sources is not null &&
            !_sources.TryGetSource(PanelSourceId.Local, out _))
        {
            throw new PanelSourceUnavailableException(PanelSourceId.Local);
        }

        IFilePanelSource? source = isLocal ? null : ResolveSource(location.SourceId);
        string sourcePath = source is null ? request.DirectoryPath : FromProvider(source, () => source.NormalizePath(location.SourcePath));

        // 1. Read raw entries (no .., no filtering, no sorting)
        var raw = preloadedEntries ?? (isLocal
            ? _fs.ReadDirectory(sourcePath)
            : FromProvider(source!, () => source!.EnumerateDirectory(sourcePath, cancellationToken)));

        // 2. Map + detect mount points
        var items = new List<FilePanelItem>(raw.Count + 1);
        foreach (var item in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.IsParentDirectory)
                continue; // skip any .. that leaked from old fs

            if (opts.DetectVolumeMountPoints && item.IsDirectory && _mountPoints != null)
            {
                var mp = TryGetMountPoint(item.FullPath);
                if (mp is not null)
                {
                    items.Add(new FilePanelItem
                    {
                        Name = item.Name,
                        FullPath = item.FullPath,
                        SourceId = location.SourceId,
                        IsDirectory = item.IsDirectory,
                        Size = item.Size,
                        LastWriteTime = item.LastWriteTime,
                        Attributes = item.Attributes,
                        IsParentDirectory = item.IsParentDirectory,
                        IsVolumeMountPoint = true,
                        MountedVolumeName = mp.VolumeName,
                        MountedVolumePath = mp.VolumePath,
                    });
                    continue;
                }
            }
            items.Add(isLocal || item.SourceId == location.SourceId
                ? item
                : CloneForSource(item, location.SourceId));
        }

        // 3. Visibility filter
        if (!opts.ShowHiddenAndSystemFiles)
        {
            items = items.Where(i =>
                (i.Attributes & FileAttributes.Hidden) == 0 &&
                (i.Attributes & FileAttributes.System) == 0).ToList();
        }

        // 4. Add .. (or not)
        bool isRoot = source is null ? IsRootDirectory(sourcePath, opts) : FromProvider(source, () => source.IsRootPath(sourcePath));
        string? sourceParentPath = source is null ? null : FromProvider(source, () => source.GetParentPath(sourcePath));
        string? localParentPath = isLocal ? _pathSemantics.GetParentPath(sourcePath) : null;
        bool showParentDirectory = ShouldShowParentDirectory(
            isRoot,
            isLocal,
            sourceParentPath,
            opts);
        if (showParentDirectory)
        {
            string parentPath = isRoot && isLocal
                ? sourcePath
                : sourceParentPath ?? localParentPath ?? sourcePath;

            items.Insert(0, new FilePanelItem
            {
                Name = "..",
                FullPath = parentPath,
                SourceId = location.SourceId,
                IsDirectory = true,
                IsParentDirectory = true,
                Attributes = FileAttributes.Directory,
            });
        }

        // 5. Sort
        var sortOptions = new PanelSortOptions
        {
            SortFoldersByExtension = opts.SortFoldersByExtension,
            KeepParentDirectoryFirst = true,
            DirectoriesFirst = true,
        };
        var sorted = _sort.Sort(items, request.SortMode, request.SortDescending, sortOptions);

        // 6. Calculate summary
        long totalBytes = 0;
        int fileCount = 0;
        int dirCount = 0;
        int selCount = 0;
        long selBytes = 0;

        foreach (var item in sorted)
        {
            if (item.IsParentDirectory) continue;
            if (item.IsDirectory) dirCount++;
            else { fileCount++; totalBytes += item.Size ?? 0; }
            if (request.SelectedPaths.Contains(item.FullPath))
            {
                selCount++;
                if (!item.IsDirectory) selBytes += item.Size ?? 0;
            }
        }

        VolumeSpaceInfo? vsInfo = null;
        bool vsUnavail = false;
        if (opts.ShowFreeSize && _volumeInfo is not null)
        {
            try { vsInfo = isLocal ? _volumeInfo.GetSpaceInfo(sourcePath) : null; }
            catch { vsUnavail = true; }
        }

        var summary = new PanelSummary
        {
            VisibleItemCount = fileCount + dirCount,
            FileCount = fileCount,
            DirectoryCount = dirCount,
            TotalFileSize = totalBytes,
            SelectedCount = selCount,
            SelectedFileSize = selBytes,
            VolumeSpace = vsInfo,
            VolumeSpaceUnavailable = vsUnavail,
        };

        cancellationToken.ThrowIfCancellationRequested();
        return new PanelView
        {
            Items = sorted,
            Summary = summary,
            AutoRefreshState = new PanelAutoRefreshState(),
            IsRootDirectory = isRoot,
            ProviderCapabilities = source is null ? PanelProviderCapabilities.LocalFileSystem : FromProvider(source, () => source.Capabilities),
        };
    }

    // Protect the panel from arbitrary exceptions thrown by third-party source calls.
    // Do not mask exceptions in core sorting, layout or state updates.
    private static T FromProvider<T>(IFilePanelSource source, Func<T> operation)
    {
        try { return operation(); }
        catch (Exception error) when (error is not IOException
            and not UnauthorizedAccessException
            and not OperationCanceledException
            and not PanelSourceUnavailableException)
        {
            throw new IOException($"Panel provider '{source.SourceId}' failed: {error.Message}", error);
        }
    }

    private IFilePanelSource ResolveSource(PanelSourceId sourceId)
    {
        if (_sources is null)
            throw new PanelSourceUnavailableException(sourceId);

        if (_sources.TryGetSource(sourceId, out var source))
            return source;

        throw new PanelSourceUnavailableException(sourceId);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static bool ShouldShowParentDirectory(
        bool isRoot,
        bool isLocal,
        string? sourceParentPath,
        AppSettings.PanelOptionsSettings options)
    {
        if (!isRoot)
            return true;

        if (isLocal)
            return options.ShowParentDirectoryInRootFolders;

        return sourceParentPath is not null;
    }

    private bool IsRootDirectory(string path, AppSettings.PanelOptionsSettings options)
    {
        if (_pathSemantics.IsRoot(path))
            return true;

        if (options.DetectVolumeMountPoints && _mountPoints is not null)
        {
            var mountInfo = TryGetMountPoint(path);
            if (mountInfo is not null)
                return true;
        }

        return false;
    }

    private VolumeMountPointInfo? TryGetMountPoint(string dirPath)
    {
        if (_mountPoints is null) return null;
        try
        {
            var info = _mountPoints.GetMountPointInfo(dirPath);
            return info.IsVolumeMountPoint ? info : null;
        }
        catch { return null; }
    }

    private static FilePanelItem CloneForSource(FilePanelItem item, PanelSourceId sourceId) =>
        new()
        {
            Name = item.Name,
            FullPath = item.FullPath,
            SourceId = sourceId,
            IsDirectory = item.IsDirectory,
            Size = item.Size,
            LastWriteTime = item.LastWriteTime,
            Attributes = item.Attributes,
            IsParentDirectory = item.IsParentDirectory,
            IsVolumeMountPoint = item.IsVolumeMountPoint,
            MountedVolumeName = item.MountedVolumeName,
            MountedVolumePath = item.MountedVolumePath,
        };
}
