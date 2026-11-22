using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.FileSystem;

public sealed class LocalFilePanelSource : IFilePanelSource
{
    private static readonly char[] LocalPathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private readonly IFileSystemService _fileSystem;
    private readonly IVolumeMountPointService? _mountPoints;
    private readonly ILocalPathNormalizer _localPathNormalizer;

    public LocalFilePanelSource(
        IFileSystemService fileSystem,
        IVolumeMountPointService? mountPoints = null,
        ILocalPathNormalizer? localPathNormalizer = null)
    {
        _fileSystem = fileSystem;
        _mountPoints = mountPoints;
        _localPathNormalizer = localPathNormalizer ?? LocalPathNormalizer.Current;
    }

    public PanelSourceId SourceId => PanelSourceId.Local;

    public string DisplayName => "Local";

    public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.LocalFileSystem;

    public IReadOnlyCollection<char> PathSeparators => LocalPathSeparators;

    public string NormalizePath(string sourcePath) => _localPathNormalizer.Normalize(sourcePath);

    public bool IsRootPath(string sourcePath) =>
        Path.GetPathRoot(NormalizePath(sourcePath)) == NormalizePath(sourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

    public string? GetParentPath(string sourcePath) =>
        Directory.GetParent(NormalizePath(sourcePath))?.FullName;

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<FilePanelItem> items = _fileSystem.ReadDirectory(NormalizePath(sourcePath));
        if (_mountPoints is null)
            return items;

        return items.Select(DecorateMountPoint).ToArray();
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);

        // Exists() hides broken links, but the planner must see their attributes.
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        bool isLink = (attributes & FileAttributes.ReparsePoint) != 0;
        if (isDirectory)
        {
            var directory = new DirectoryInfo(path);
            return DecorateMountPoint(new FilePanelItem
            {
                Name = directory.Name, FullPath = directory.FullName,
                SourceId = SourceId, IsDirectory = true, Size = null,
                LastWriteTime = isLink && !directory.Exists ? DateTime.MinValue : directory.LastWriteTime,
                Attributes = attributes, IsParentDirectory = false,
            });
        }

        var file = new FileInfo(path);
        return new FilePanelItem
        {
            Name = file.Name, FullPath = file.FullName,
            SourceId = SourceId, IsDirectory = false,
            Size = isLink && !file.Exists ? 0 : file.Length,
            LastWriteTime = isLink && !file.Exists ? DateTime.MinValue : file.LastWriteTime,
            Attributes = attributes, IsParentDirectory = false,
        };
    }

    public Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = File.Open(
            NormalizePath(sourcePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Stream stream = File.Open(
            path,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        return Task.FromResult(stream);
    }

    public Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(NormalizePath(sourcePath));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);
        if (File.Exists(path))
            File.Delete(path);
        else if (Directory.Exists(path))
            Directory.Delete(path, recursive);

        return Task.CompletedTask;
    }

    public Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string source = NormalizePath(sourcePath);
        string target = NormalizePath(newSourcePath);

        if (File.Exists(source))
            File.Move(source, target);
        else
            Directory.Move(source, target);

        return Task.CompletedTask;
    }

    private FilePanelItem DecorateMountPoint(FilePanelItem item)
    {
        if (_mountPoints is null || !item.IsDirectory || item.IsVolumeMountPoint)
            return item;

        try
        {
            VolumeMountPointInfo info = _mountPoints.GetMountPointInfo(item.FullPath);
            if (!info.IsVolumeMountPoint)
                return item;

            return new FilePanelItem
            {
                Name = item.Name,
                FullPath = item.FullPath,
                SourceId = item.SourceId,
                IsDirectory = item.IsDirectory,
                Size = item.Size,
                LastWriteTime = item.LastWriteTime,
                Attributes = item.Attributes,
                IsParentDirectory = item.IsParentDirectory,
                IsVolumeMountPoint = true,
                MountedVolumeName = info.VolumeName,
                MountedVolumePath = info.VolumePath,
            };
        }
        catch
        {
            return item;
        }
    }
}
