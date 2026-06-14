using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class UnixVolumeService : IVolumeService
{
    public IReadOnlyList<FileSystemVolume> GetVolumes()
    {
        var volumes = new List<FileSystemVolume>
        {
            new()
            {
                Id = "/",
                DisplayName = "/",
                RootPath = "/",
                Kind = VolumeKind.Fixed,
                Status = Directory.Exists("/") ? VolumeStatus.Ready : VolumeStatus.Error,
                Shortcut = null,
            },
        };

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return volumes; }

        foreach (var drive in drives)
        {
            string rootPath = drive.RootDirectory.FullName;
            if (volumes.Any(v => string.Equals(v.RootPath, rootPath, StringComparison.Ordinal)))
                continue;

            volumes.Add(BuildVolume(drive));
        }

        return volumes;
    }

    private static FileSystemVolume BuildVolume(DriveInfo drive)
    {
        var status = VolumeStatus.Unchecked;
        long? total = null;
        long? free = null;

        try
        {
            status = drive.IsReady ? VolumeStatus.Ready : VolumeStatus.NotReady;
            if (drive.IsReady)
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            status = VolumeStatus.Error;
        }

        return new FileSystemVolume
        {
            Id = drive.Name,
            DisplayName = drive.Name.TrimEnd('/'),
            RootPath = drive.RootDirectory.FullName,
            Kind = drive.DriveType == DriveType.Network ? VolumeKind.Network : VolumeKind.MountPoint,
            Status = status,
            TotalBytes = total,
            FreeBytes = free,
            Shortcut = null,
        };
    }
}
