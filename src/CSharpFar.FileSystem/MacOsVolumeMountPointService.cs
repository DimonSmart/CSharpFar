using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class MacOsVolumeMountPointService : IVolumeMountPointService
{
    private readonly IVolumeService _volumeService;
    public MacOsVolumeMountPointService() : this(new MacOsVolumeService()) { }
    internal MacOsVolumeMountPointService(IVolumeService volumeService) => _volumeService = volumeService;

    public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return new VolumeMountPointInfo { IsVolumeMountPoint = false };
        string path = MacOsVolumeService.Normalize(Path.GetFullPath(directoryPath));
        FileSystemVolume? volume = _volumeService.GetVolumes().FirstOrDefault(volume => volume.RootPath == path);
        return volume is null ? new VolumeMountPointInfo { IsVolumeMountPoint = false }
            : new VolumeMountPointInfo { IsVolumeMountPoint = true, VolumeName = volume.DisplayName, VolumePath = volume.RootPath };
    }
}
