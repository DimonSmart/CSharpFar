using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IVolumeMountPointService
{
    VolumeMountPointInfo GetMountPointInfo(string directoryPath);
}
