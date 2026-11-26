using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional async-first extension of the binary-compatible file-panel SDK.
/// Implementations must return promptly even when the underlying I/O is slow.
/// Cancellation is cooperative; a returned task owns the operation until it
/// actually finishes, including any cleanup. Path operations remain synchronous.
/// </summary>
public interface IAsyncFilePanelSource : IFilePanelSource
{
    Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(
        string sourcePath, CancellationToken cancellationToken = default);

    Task<FilePanelItem?> GetItemAsync(
        string sourcePath, CancellationToken cancellationToken = default);
}
