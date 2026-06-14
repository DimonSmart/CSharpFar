using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class PanelViewBuilder : IPanelViewBuilder
{
    private readonly IFileSystemService     _fs;
    private readonly IPanelSortService      _sort;
    private readonly IVolumeInfoService?    _volumeInfo;
    private readonly IVolumeMountPointService? _mountPoints;
    private readonly IFilePanelSourceRegistry? _sources;
    private readonly IPanelPathSemantics _pathSemantics;

    public PanelViewBuilder(
        IFileSystemService        fs,
        IPanelSortService         sort,
        IVolumeInfoService?       volumeInfo   = null,
        IVolumeMountPointService? mountPoints  = null,
        IFilePanelSourceRegistry? sources      = null,
        IPanelPathSemantics?      pathSemantics = null)
    {
        _fs          = fs;
        _sort        = sort;
        _volumeInfo  = volumeInfo;
        _mountPoints = mountPoints;
        _sources     = sources;
        _pathSemantics = pathSemantics ?? PanelPathSemantics.Current;
    }

    public PanelView Build(PanelViewRequest request)
    {
        var opts = request.Options;
        var location = request.Location ?? PanelLocation.Local(request.DirectoryPath);
        bool isLocal = location.SourceId == PanelSourceId.Local;
        IFilePanelSource? source = isLocal ? null : _sources?.GetSource(location.SourceId);
        string sourcePath = source?.NormalizePath(location.SourcePath) ?? request.DirectoryPath;

        // 1. Read raw entries (no .., no filtering, no sorting)
        var raw = isLocal
            ? _fs.ReadDirectory(sourcePath)
            : source!.EnumerateDirectory(sourcePath);

        // 2. Map + detect mount points
        var items = new List<FilePanelItem>(raw.Count + 1);
        foreach (var item in raw)
        {
            if (item.IsParentDirectory)
                continue; // skip any .. that leaked from old fs

            if (opts.DetectVolumeMountPoints && item.IsDirectory && _mountPoints != null)
            {
                var mp = TryGetMountPoint(item.FullPath);
                if (mp is not null)
                {
                    items.Add(new FilePanelItem
                    {
                        Name               = item.Name,
                        FullPath           = item.FullPath,
                        SourceId           = location.SourceId,
                        IsDirectory        = item.IsDirectory,
                        Size               = item.Size,
                        LastWriteTime      = item.LastWriteTime,
                        Attributes         = item.Attributes,
                        IsParentDirectory  = item.IsParentDirectory,
                        IsVolumeMountPoint = true,
                        MountedVolumeName  = mp.VolumeName,
                        MountedVolumePath  = mp.VolumePath,
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
        bool isRoot = source?.IsRootPath(sourcePath) ?? IsRootDirectory(sourcePath, opts);
        string? sourceParentPath = source?.GetParentPath(sourcePath);
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
                Name              = "..",
                FullPath          = parentPath,
                SourceId          = location.SourceId,
                IsDirectory       = true,
                IsParentDirectory = true,
                Attributes        = FileAttributes.Directory,
            });
        }

        // 5. Sort
        var sortOptions = new PanelSortOptions
        {
            SortFoldersByExtension   = opts.SortFoldersByExtension,
            KeepParentDirectoryFirst = true,
            DirectoriesFirst         = true,
        };
        var sorted = _sort.Sort(items, request.SortMode, request.SortDescending, sortOptions);

        // 6. Calculate summary
        long totalBytes = 0;
        int  fileCount  = 0;
        int  dirCount   = 0;
        int  selCount   = 0;
        long selBytes   = 0;

        foreach (var item in sorted)
        {
            if (item.IsParentDirectory) continue;
            if (item.IsDirectory)       dirCount++;
            else { fileCount++; totalBytes += item.Size ?? 0; }
            if (request.SelectedPaths.Contains(item.FullPath))
            {
                selCount++;
                if (!item.IsDirectory) selBytes += item.Size ?? 0;
            }
        }

        VolumeSpaceInfo? vsInfo  = null;
        bool             vsUnavail = false;
        if (opts.ShowFreeSize && _volumeInfo is not null)
        {
            try { vsInfo = isLocal ? _volumeInfo.GetSpaceInfo(sourcePath) : null; }
            catch { vsUnavail = true; }
        }

        var summary = new PanelSummary
        {
            VisibleItemCount      = fileCount + dirCount,
            FileCount             = fileCount,
            DirectoryCount        = dirCount,
            TotalFileSize         = totalBytes,
            SelectedCount         = selCount,
            SelectedFileSize      = selBytes,
            VolumeSpace           = vsInfo,
            VolumeSpaceUnavailable = vsUnavail,
        };

        return new PanelView
        {
            Items            = sorted,
            Summary          = summary,
            AutoRefreshState = new PanelAutoRefreshState(),
            IsRootDirectory  = isRoot,
            ProviderCapabilities = source?.Capabilities ?? PanelProviderCapabilities.LocalFileSystem,
        };
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
