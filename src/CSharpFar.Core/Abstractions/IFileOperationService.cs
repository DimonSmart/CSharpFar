using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileOperationService
{
    /// <param name="onProgress">Called with the current item name before each copy.</param>
    /// <param name="onConflict">Called when a destination file already exists; returns the chosen action.</param>
    Task CopyAsync(
        IReadOnlyList<string> sources,
        string destination,
        Action<string>? onProgress = null,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default);

    Task MoveAsync(IReadOnlyList<string> sources, string destination, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
    void CreateDirectory(string path);
}
