using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IVolumeService
{
    IReadOnlyList<FileSystemVolume> GetVolumes();
}
