using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class SearchResultsDeleteSupport
{
    public static IReadOnlyList<string> CollapseNestedSources(IReadOnlyList<string> sources)
    {
        if (sources.Count <= 1)
            return sources;

        return sources
            .Where((source, index) => !sources.Where((_, otherIndex) => otherIndex != index)
                .Any(candidate => IsSameOrDescendant(source, candidate)))
            .ToList();
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

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Length <= normalizedRoot.Length)
        {
            return false;
        }

        char separator = normalizedPath[normalizedRoot.Length];
        return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
