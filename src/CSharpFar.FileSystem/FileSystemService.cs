using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<FilePanelItem> ReadDirectory(string path)
    {
        var result    = new List<FilePanelItem>();
        var directory = new DirectoryInfo(path);

        foreach (var dir in directory.GetDirectories())
        {
            result.Add(new FilePanelItem
            {
                Name          = dir.Name,
                FullPath      = dir.FullName,
                IsDirectory   = true,
                LastWriteTime = SafeGet(() => dir.LastWriteTime, DateTime.MinValue),
                Attributes    = SafeGet(() => dir.Attributes, FileAttributes.Directory),
            });
        }

        foreach (var file in directory.GetFiles())
        {
            result.Add(new FilePanelItem
            {
                Name          = file.Name,
                FullPath      = file.FullName,
                IsDirectory   = false,
                Size          = SafeGet(() => file.Length, 0L),
                LastWriteTime = SafeGet(() => file.LastWriteTime, DateTime.MinValue),
                Attributes    = SafeGet(() => file.Attributes, FileAttributes.Normal),
            });
        }

        return result;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path)      => File.Exists(path);

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}
