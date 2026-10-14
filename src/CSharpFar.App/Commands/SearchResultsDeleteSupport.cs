using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.App.Commands;

internal static class SearchResultsDeleteSupport
{
    public static IReadOnlyList<string> CollapseNestedSources(IReadOnlyList<string> sources)
    {
        if (sources.Count <= 1)
            return sources;

        var roots = new List<string>(sources.Count);
        foreach (string source in sources)
        {
            if (roots.Any(root => IsSameOrDescendant(source, root)))
                continue;

            roots.RemoveAll(root => IsSameOrDescendant(root, source));
            roots.Add(source);
        }

        return roots;
    }

    public static void Reconcile(
        FilePanelState state,
        IReadOnlyCollection<string> requestedRoots,
        FileOperationResult result)
    {
        if (state.SearchRequest is null || requestedRoots.Count == 0)
            return;

        bool operationCompletedCleanly = !result.Cancelled && result.Errors.Count == 0;
        state.Items.RemoveAll(item =>
            requestedRoots.Any(root => IsSameOrDescendant(item.FullPath, root)) &&
            (operationCompletedCleanly || !PathExists(item.FullPath)));
    }

    private static bool PathExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        string normalizedPath = TrimTrailingSeparators(path);
        string normalizedRoot = TrimTrailingSeparators(root);

        StringComparer comparer = LocalFileSystemPathComparer.Current;
        if (comparer.Equals(normalizedPath, normalizedRoot))
            return true;

        if (normalizedPath.Length <= normalizedRoot.Length)
            return false;

        char separator = normalizedPath[normalizedRoot.Length];
        if (separator != Path.DirectorySeparatorChar && separator != Path.AltDirectorySeparatorChar)
            return false;

        return comparer.Equals(normalizedPath[..normalizedRoot.Length], normalizedRoot);
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
