using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional asynchronous enumeration for remote/plugin sources. The application
/// never calls this method on the terminal thread. Existing IFilePanelSource
/// implementations remain supported through background synchronous enumeration.
/// </summary>
public interface IAsyncFilePanelSource : IFilePanelSource
{
    Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(
        string sourcePath, CancellationToken cancellationToken = default);
}
