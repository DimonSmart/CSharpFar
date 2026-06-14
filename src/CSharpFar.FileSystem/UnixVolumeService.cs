using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class UnixVolumeService : IVolumeService
{
    private readonly UnixMountInfoReader _mountInfoReader;

    public UnixVolumeService()
        : this(new UnixMountInfoReader())
    {
    }

    internal UnixVolumeService(UnixMountInfoReader mountInfoReader)
    {
        _mountInfoReader = mountInfoReader;
    }

    public IReadOnlyList<FileSystemVolume> GetVolumes()
    {
        var mountEntries = _mountInfoReader.Read()
            .Where(UnixMountInfoReader.IsUserVisible)
            .GroupBy(static entry => UnixMountInfoReader.NormalizeMountPoint(entry.MountPoint), StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.MountPoint, StringComparer.Ordinal)
            .ToList();

        if (mountEntries.Count > 0)
            return mountEntries.Select(BuildVolume).ToList();

        return [BuildRootVolume()];
    }

    private static FileSystemVolume BuildVolume(UnixMountInfoEntry entry)
    {
        string rootPath = UnixMountInfoReader.NormalizeMountPoint(entry.MountPoint);
        TryGetSpace(rootPath, out long? total, out long? free, out var status);

        return new FileSystemVolume
        {
            Id = rootPath,
            DisplayName = rootPath,
            RootPath = rootPath,
            Kind = GetKind(entry),
            Status = status,
            TotalBytes = total,
            FreeBytes = free,
            Shortcut = null,
        };
    }

    private static FileSystemVolume BuildRootVolume()
    {
        TryGetSpace("/", out long? total, out long? free, out var status);
        return new FileSystemVolume
        {
            Id = "/",
            DisplayName = "/",
            RootPath = "/",
            Kind = VolumeKind.Fixed,
            Status = status,
            TotalBytes = total,
            FreeBytes = free,
            Shortcut = null,
        };
    }

    private static VolumeKind GetKind(UnixMountInfoEntry entry)
    {
        if (entry.MountPoint == "/")
            return VolumeKind.Fixed;
        if (UnixMountInfoReader.IsNetworkFileSystem(entry.FileSystemType))
            return VolumeKind.Network;
        if (entry.FileSystemType is "tmpfs" or "ramfs")
            return VolumeKind.Ram;
        return VolumeKind.MountPoint;
    }

    private static void TryGetSpace(string path, out long? total, out long? free, out VolumeStatus status)
    {
        total = null;
        free = null;
        status = VolumeStatus.Unchecked;

        try
        {
            var drive = new DriveInfo(path);
            status = drive.IsReady ? VolumeStatus.Ready : VolumeStatus.NotReady;
            if (drive.IsReady)
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            status = Directory.Exists(path) ? VolumeStatus.Ready : VolumeStatus.Error;
        }
    }
}
