namespace CSharpFar.App.Viewer;

/// <summary>
/// Snapshot of the directory size calculation progress.
/// </summary>
/// <param name="Size">Current total size in bytes (may be partial while <see cref="IsCompleted"/> is false).</param>
/// <param name="IsCompleted">True when the full tree has been scanned.</param>
/// <param name="Errors">Paths that could not be accessed during the scan.</param>
internal sealed record DirectorySizeState(long Size, bool IsCompleted, IReadOnlyList<string> Errors);
