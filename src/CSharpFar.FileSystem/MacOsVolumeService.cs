using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>Full macOS mount catalog. Only the UI applies visibility filtering.</summary>
public sealed class MacOsVolumeService : IVolumeService
{
    private readonly IMacOsMountProvider? _mounts;
    private readonly Func<DriveInfo[]> _getDrives;
    private readonly object _gate = new();
    private IReadOnlyList<FileSystemVolume>? _snapshot;
    private DateTime _expiresUtc;

    public MacOsVolumeService() : this(null, DriveInfo.GetDrives) { }
    public MacOsVolumeService(IMacOsMountProvider mounts) : this(mounts ?? throw new ArgumentNullException(nameof(mounts)), DriveInfo.GetDrives) { }
    internal MacOsVolumeService(Func<DriveInfo[]> getDrives) : this(null, getDrives) { }

    private MacOsVolumeService(IMacOsMountProvider? mounts, Func<DriveInfo[]> getDrives)
    {
        _mounts = mounts;
        _getDrives = getDrives ?? throw new ArgumentNullException(nameof(getDrives));
    }

    public IReadOnlyList<FileSystemVolume> GetVolumes()
    {
        lock (_gate)
        {
            if (_snapshot is not null && DateTime.UtcNow < _expiresUtc)
                return _snapshot;

            IReadOnlyList<FileSystemVolume>? native = null;
            if (_mounts is not null)
            {
                try { native = BuildNative(_mounts.GetMounts()); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or
                    DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.SEHException)
                {
                    // Only a failing native enumerator requires the DriveInfo fallback.
                }
            }

            _snapshot = native is { Count: > 0 } ? native : BuildFallback();
            _expiresUtc = DateTime.UtcNow.AddSeconds(2);
            return _snapshot;
        }
    }

    public void Invalidate()
    {
        lock (_gate) _expiresUtc = DateTime.MinValue;
    }

    internal static IReadOnlyList<FileSystemVolume> BuildNative(IReadOnlyList<MacOsMount> mounts)
    {
        ArgumentNullException.ThrowIfNull(mounts);
        var result = new Dictionary<string, FileSystemVolume>(StringComparer.Ordinal);
        foreach (MacOsMount mount in mounts)
        {
            if (string.IsNullOrWhiteSpace(mount.RootPath)) continue;
            string path = Normalize(mount.RootPath);
            if (!path.StartsWith('/')) continue;
            if (result.ContainsKey(path)) continue;
            bool network = mount.FileSystemType is "smbfs" or "nfs" or "webdav" or "afpfs";
            result.Add(path, new FileSystemVolume
            {
                Id = path,
                RootPath = path,
                DisplayName = string.IsNullOrWhiteSpace(mount.DisplayName)
                    ? (path == "/" ? "/" : Path.GetFileName(path))
                    : mount.DisplayName,
                Visibility = path == "/" ? VolumeVisibility.User :
                    mount.IsHidden ? VolumeVisibility.System : VolumeVisibility.User,
                Kind = network ? VolumeKind.Network : VolumeKind.Fixed,
                Status = VolumeStatus.Unchecked,
                TotalBytes = mount.TotalBytes is >= 0 ? mount.TotalBytes : null,
                FreeBytes = mount.FreeBytes is >= 0 ? mount.FreeBytes : null,
            });
        }
        EnsureRoot(result);
        return Sort(result.Values);
    }

    private IReadOnlyList<FileSystemVolume> BuildFallback()
    {
        var result = new Dictionary<string, FileSystemVolume>(StringComparer.Ordinal);
        DriveInfo[] drives;
        try { drives = _getDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { drives = []; }

        foreach (DriveInfo drive in drives)
        {
            try
            {
                string path = Normalize(drive.RootDirectory.FullName);
                if (result.ContainsKey(path)) continue;
                // A failed stat is not a reason to erase a mount from the catalog.
                long? total = null, free = null;
                VolumeStatus status = VolumeStatus.Unchecked;
                if (drive.DriveType != DriveType.Network && drive.IsReady)
                {
                    status = VolumeStatus.Ready;
                    try { total = drive.TotalSize; free = drive.AvailableFreeSpace; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                result.Add(path, new FileSystemVolume
                {
                    Id = path, RootPath = path, DisplayName = path,
                    Kind = drive.DriveType == DriveType.Network ? VolumeKind.Network : VolumeKind.Fixed,
                    Status = status, TotalBytes = total, FreeBytes = free,
                    Visibility = path == "/" ? VolumeVisibility.User : VolumeVisibility.Unknown,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Unreadable drive metadata does not prevent enumeration of other drives.
            }
        }
        EnsureRoot(result);
        return Sort(result.Values);
    }

    private static void EnsureRoot(Dictionary<string, FileSystemVolume> result)
    {
        if (result.ContainsKey("/")) return;
        result.Add("/", new FileSystemVolume
        {
            Id = "/", RootPath = "/", DisplayName = "/", Kind = VolumeKind.Fixed,
            Status = VolumeStatus.Unchecked, Visibility = VolumeVisibility.User,
        });
    }

    private static IReadOnlyList<FileSystemVolume> Sort(IEnumerable<FileSystemVolume> volumes) =>
        volumes.OrderBy(volume => volume.RootPath == "/" ? 0 : 1)
            .ThenBy(volume => volume.RootPath, StringComparer.Ordinal).ToArray();

    internal static string Normalize(string path) => path == "/" ? path : path.TrimEnd('/');
}
