using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    private readonly bool _showHiddenFiles;
    private readonly bool _showSystemFiles;

    public FileSystemService(bool showHiddenFiles = true, bool showSystemFiles = true)
    {
        _showHiddenFiles = showHiddenFiles;
        _showSystemFiles = showSystemFiles;
    }

    public IReadOnlyList<FilePanelItem> ReadDirectory(string path)
    {
        var result = new List<FilePanelItem>();

        // Parent entry (..)
        var parentPath = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parentPath != null)
        {
            result.Add(new FilePanelItem
            {
                Name = "..",
                FullPath = parentPath,
                IsDirectory = true,
                IsParentDirectory = true,
            });
        }

        var directory = new DirectoryInfo(path);

        // Directories (sorted by name)
        var dirs = directory
            .GetDirectories()
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var dir in dirs)
        {
            var attrs = SafeGet(() => dir.Attributes, FileAttributes.Directory);
            if (!_showHiddenFiles && (attrs & FileAttributes.Hidden) != 0) continue;
            if (!_showSystemFiles && (attrs & FileAttributes.System) != 0) continue;

            result.Add(new FilePanelItem
            {
                Name = dir.Name,
                FullPath = dir.FullName,
                IsDirectory = true,
                LastWriteTime = SafeGet(() => dir.LastWriteTime, DateTime.MinValue),
                Attributes = attrs,
            });
        }

        // Files (sorted by name)
        var files = directory
            .GetFiles()
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var attrs = SafeGet(() => file.Attributes, FileAttributes.Normal);
            if (!_showHiddenFiles && (attrs & FileAttributes.Hidden) != 0) continue;
            if (!_showSystemFiles && (attrs & FileAttributes.System) != 0) continue;

            result.Add(new FilePanelItem
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Size = SafeGet(() => file.Length, 0L),
                LastWriteTime = SafeGet(() => file.LastWriteTime, DateTime.MinValue),
                Attributes = attrs,
            });
        }

        return result;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}
