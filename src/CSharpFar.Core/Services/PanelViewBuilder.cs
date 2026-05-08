using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class PanelViewBuilder : IPanelViewBuilder
{
    private readonly IFileSystemService     _fs;
    private readonly IPanelSortService      _sort;
    private readonly IVolumeInfoService?    _volumeInfo;
    private readonly IVolumeMountPointService? _mountPoints;

    public PanelViewBuilder(
        IFileSystemService        fs,
        IPanelSortService         sort,
        IVolumeInfoService?       volumeInfo   = null,
        IVolumeMountPointService? mountPoints  = null)
    {
        _fs          = fs;
        _sort        = sort;
        _volumeInfo  = volumeInfo;
        _mountPoints = mountPoints;
    }

    public PanelView Build(PanelViewRequest request)
    {
        var opts = request.Options;

        // 1. Read raw entries (no .., no filtering, no sorting)
        var raw = _fs.ReadDirectory(request.DirectoryPath);

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
            items.Add(item);
        }

        // 3. Visibility filter
        if (!opts.ShowHiddenAndSystemFiles)
        {
            items = items.Where(i =>
                (i.Attributes & FileAttributes.Hidden) == 0 &&
                (i.Attributes & FileAttributes.System) == 0).ToList();
        }

        // 4. Add .. (or not)
        bool isRoot = IsRootDirectory(request.DirectoryPath, opts);
        if (!isRoot || opts.ShowParentDirectoryInRootFolders)
        {
            string parentPath = isRoot
                ? request.DirectoryPath
                : Path.GetDirectoryName(
                    request.DirectoryPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))
                  ?? request.DirectoryPath;

            items.Insert(0, new FilePanelItem
            {
                Name              = "..",
                FullPath          = parentPath,
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
            try { vsInfo = _volumeInfo.GetSpaceInfo(request.DirectoryPath); }
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
        };
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private bool IsRootDirectory(string path, AppSettings.PanelOptionsSettings options)
    {
        string norm = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Windows drive root: "C:" after trimming
        if (norm.Length == 2 && char.IsLetter(norm[0]) && norm[1] == ':')
            return true;

        // Path.GetDirectoryName returns null for root paths
        string? parent = Path.GetDirectoryName(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is null)
            return true;

        // UNC share root: \\server\share
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            string[] parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2) return true;
        }

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
}
