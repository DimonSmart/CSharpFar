using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IVolumeInfoService
{
    VolumeSpaceInfo GetSpaceInfo(string path);
}
