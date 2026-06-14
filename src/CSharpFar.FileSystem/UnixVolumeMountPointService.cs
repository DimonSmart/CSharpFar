using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class UnixVolumeMountPointService : IVolumeMountPointService
{
    public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return NotMounted();

            string fullPath = Path.GetFullPath(directoryPath);
            if (fullPath == Path.GetPathRoot(fullPath))
            {
                return new VolumeMountPointInfo
                {
                    IsVolumeMountPoint = true,
                    VolumeName = fullPath,
                    VolumePath = fullPath,
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
