using System.Collections.Concurrent;

namespace CSharpFar.App.Search;

/// <summary>
/// Recursively searches for files matching a glob mask.
/// Thread-safe: Collect can be called from a background thread.
/// </summary>
public static class FileSearcher
{
    /// <summary>
    /// Searches rootDir recursively for files matching mask.
    /// Returns results sorted by path. Silently skips inaccessible directories.
    /// </summary>
    public static IReadOnlyList<string> Search(
        string rootDir,
        string mask,
        CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<string>();
        try { Collect(rootDir, mask, bag, ct); }
        catch (OperationCanceledException) { }
        return bag.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void Collect(
        string dir,
        string mask,
        ConcurrentBag<string> bag,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, mask))
            {
                ct.ThrowIfCancellationRequested();
                bag.Add(f);
            }
            foreach (var sub in Directory.EnumerateDirectories(dir))
                Collect(sub, mask, bag, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* skip inaccessible dirs/files */ }
    }
}
