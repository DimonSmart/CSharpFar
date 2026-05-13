using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class LocalFilePanelSource : IFilePanelSource
{
    private readonly IFileSystemService _fileSystem;

    public LocalFilePanelSource(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public PanelSourceId SourceId => PanelSourceId.Local;

    public string DisplayName => "Local";

    public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.LocalFileSystem;

    public string NormalizePath(string sourcePath) => Path.GetFullPath(sourcePath);

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
        return _fileSystem.ReadDirectory(NormalizePath(sourcePath));
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = NormalizePath(sourcePath);

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return new FilePanelItem
            {
                Name = file.Name,
                FullPath = file.FullName,
                SourceId = SourceId,
                IsDirectory = false,
                Size = file.Length,
                LastWriteTime = file.LastWriteTime,
                Attributes = file.Attributes,
                IsParentDirectory = false,
            };
        }

        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            return new FilePanelItem
            {
                Name = directory.Name,
                FullPath = directory.FullName,
                SourceId = SourceId,
                IsDirectory = true,
                Size = null,
                LastWriteTime = directory.LastWriteTime,
                Attributes = directory.Attributes,
                IsParentDirectory = false,
            };
        }

        return null;
    }

    public Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = File.Open(NormalizePath(sourcePath), FileMode.Open, FileAccess.Read, FileShare.Read);
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
}
