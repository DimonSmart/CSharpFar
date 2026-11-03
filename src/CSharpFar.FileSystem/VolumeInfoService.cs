using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class VolumeInfoService : IVolumeInfoService
{
    public VolumeSpaceInfo GetSpaceInfo(string path)
    {
        string root = Path.GetPathRoot(path) ?? path;
        var drive = new DriveInfo(root);
        return new VolumeSpaceInfo
        {
            Path = path,
            FreeBytesAvailable = drive.AvailableFreeSpace,
            TotalBytes = drive.TotalSize,
            VolumeLabel = drive.VolumeLabel,
        };
    }
}
