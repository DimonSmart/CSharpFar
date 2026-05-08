using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileSystemLocationService
{
    FileSystemLocationInfo GetLocationInfo(string path);
}
