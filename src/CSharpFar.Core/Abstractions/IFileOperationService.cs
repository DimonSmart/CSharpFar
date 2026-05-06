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

    /// <param name="destination">
    /// A directory path (move) or a plain name with no separators (rename, single source only).
    /// </param>
    /// <param name="onConflict">Called when the destination already exists.</param>
    Task MoveAsync(
        IReadOnlyList<string> sources,
        string destination,
        Func<string, ConflictChoice>? onConflict = null,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
    void CreateDirectory(string path);
}
