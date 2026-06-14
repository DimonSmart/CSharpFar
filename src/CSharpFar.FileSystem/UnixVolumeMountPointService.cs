using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class UnixVolumeMountPointService : IVolumeMountPointService
{
    private readonly UnixMountInfoReader _mountInfoReader;

    public UnixVolumeMountPointService()
        : this(new UnixMountInfoReader())
    {
    }

    internal UnixVolumeMountPointService(UnixMountInfoReader mountInfoReader)
    {
        _mountInfoReader = mountInfoReader;
    }

    public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return NotMounted();

            string fullPath = UnixMountInfoReader.NormalizeMountPoint(directoryPath);
            var entry = _mountInfoReader.Read()
                .FirstOrDefault(e => string.Equals(
                    UnixMountInfoReader.NormalizeMountPoint(e.MountPoint),
                    fullPath,
                    StringComparison.Ordinal));
            if (entry is not null)
            {
                return new VolumeMountPointInfo
                {
                    IsVolumeMountPoint = true,
                    VolumeName = entry.Source,
                    VolumePath = UnixMountInfoReader.NormalizeMountPoint(entry.MountPoint),
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
