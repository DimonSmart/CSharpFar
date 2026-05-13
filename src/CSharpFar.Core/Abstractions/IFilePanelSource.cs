using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFilePanelSource
{
    PanelSourceId SourceId { get; }
    string DisplayName { get; }
    PanelProviderCapabilities Capabilities { get; }

    string NormalizePath(string sourcePath);
    bool IsRootPath(string sourcePath);
    string? GetParentPath(string sourcePath);

    IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default);

    FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default);
}
