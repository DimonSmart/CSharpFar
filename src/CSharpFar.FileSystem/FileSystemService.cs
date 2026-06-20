using System.Security;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    private readonly Func<string, IEnumerable<string>> _enumerateChildPaths;
    private readonly Func<string, FileAttributes> _getAttributes;

    public FileSystemService()
        : this(EnumerateChildPaths, File.GetAttributes)
    {
    }

    internal FileSystemService(
        Func<string, IEnumerable<string>> enumerateChildPaths,
        Func<string, FileAttributes> getAttributes)
    {
        _enumerateChildPaths = enumerateChildPaths;
        _getAttributes = getAttributes;
    }

    public IReadOnlyList<FilePanelItem> ReadDirectory(string path)
    {
        var result = new List<FilePanelItem>();

        foreach (string entryPath in _enumerateChildPaths(path))
        {
            var item = TryCreateItem(entryPath);
            if (item is not null)
                result.Add(item);
        }

        return result;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);

    private static IEnumerable<string> EnumerateChildPaths(string path)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        return Directory.EnumerateFileSystemEntries(path, "*", options);
    }

    private FilePanelItem? TryCreateItem(string entryPath)
    {
        try
        {
            FileAttributes attributes = _getAttributes(entryPath);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;

            if (isDirectory)
            {
                var directory = new DirectoryInfo(entryPath);
                return new FilePanelItem
                {
                    Name = directory.Name,
                    FullPath = directory.FullName,
                    IsDirectory = true,
                    LastWriteTime = SafeGet(() => directory.LastWriteTime, DateTime.MinValue),
                    Attributes = attributes,
                };
            }

            var file = new FileInfo(entryPath);
            return new FilePanelItem
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Size = SafeGet(() => file.Length, 0L),
                LastWriteTime = SafeGet(() => file.LastWriteTime, DateTime.MinValue),
                Attributes = attributes,
            };
        }
        catch (Exception ex) when (IsSkippableEntryException(ex))
        {
            return null;
        }
    }

    private static bool IsSkippableEntryException(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or SecurityException;

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}
