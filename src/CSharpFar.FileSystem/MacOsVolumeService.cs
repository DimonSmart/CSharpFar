using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class MacOsVolumeService : IVolumeService
{
    private readonly Func<DriveInfo[]> _getDrives;

    public MacOsVolumeService() : this(DriveInfo.GetDrives)
    {
    }

    internal MacOsVolumeService(Func<DriveInfo[]> getDrives) => _getDrives = getDrives;

    public IReadOnlyList<FileSystemVolume> GetVolumes()
    {
        var volumes = _getDrives().Where(static drive => drive.IsReady && Directory.Exists(drive.RootDirectory.FullName))
            .Select(static drive => new FileSystemVolume
            {
                Id = Normalize(drive.RootDirectory.FullName),
                DisplayName = Normalize(drive.RootDirectory.FullName),
                RootPath = Normalize(drive.RootDirectory.FullName),
                Kind = drive.DriveType == DriveType.Network ? VolumeKind.Network : VolumeKind.Fixed,
                Status = VolumeStatus.Ready,
                TotalBytes = drive.TotalSize,
                FreeBytes = drive.AvailableFreeSpace,
            }).GroupBy(static volume => volume.RootPath, StringComparer.Ordinal).Select(static group => group.First())
            .OrderBy(static volume => volume.RootPath, StringComparer.Ordinal).ToList();
        if (volumes.All(static volume => volume.RootPath != "/")) volumes.Insert(0, CreateRoot());
        return volumes;
    }

    internal static string Normalize(string path) => path == "/" ? path : path.TrimEnd('/');

    private static FileSystemVolume CreateRoot()
    {
        var root = new DriveInfo("/");
        return new FileSystemVolume
        {
            Id = "/",
            DisplayName = "/",
            RootPath = "/",
            Kind = VolumeKind.Fixed,
            Status = root.IsReady ? VolumeStatus.Ready : VolumeStatus.NotReady,
            TotalBytes = root.IsReady ? root.TotalSize : null,
            FreeBytes = root.IsReady ? root.AvailableFreeSpace : null
        };
    }
}
