using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileSystemService
{
    IReadOnlyList<FilePanelItem> ReadDirectory(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
}
