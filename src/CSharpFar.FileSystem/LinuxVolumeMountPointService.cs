using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class LinuxVolumeMountPointService : IVolumeMountPointService
{
    private readonly LinuxMountInfoReader _mountInfoReader;

    public LinuxVolumeMountPointService()
        : this(new LinuxMountInfoReader())
    {
    }

    internal LinuxVolumeMountPointService(LinuxMountInfoReader mountInfoReader)
    {
        _mountInfoReader = mountInfoReader;
    }

    public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return NotMounted();

            string fullPath = LinuxMountInfoReader.NormalizeMountPoint(directoryPath);
            var entry = _mountInfoReader.Read()
                .FirstOrDefault(e => string.Equals(
                    LinuxMountInfoReader.NormalizeMountPoint(e.MountPoint),
                    fullPath,
                    StringComparison.Ordinal));
            if (entry is not null)
            {
                return new VolumeMountPointInfo
                {
                    IsVolumeMountPoint = true,
                    VolumeName = entry.Source,
                    VolumePath = LinuxMountInfoReader.NormalizeMountPoint(entry.MountPoint),
                };
            }
        }
        catch
        {
        }

        return NotMounted();
    }

    private static VolumeMountPointInfo NotMounted() =>
        new() { IsVolumeMountPoint = false };
}
