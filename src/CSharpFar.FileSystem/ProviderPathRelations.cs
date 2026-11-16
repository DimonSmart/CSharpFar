using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Services;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

internal static class ProviderPathRelations
{
    private static StringComparer Comparer(IFilePanelSource source) =>
        source.SourceId == PanelSourceId.Local
            ? LocalFileSystemPathComparer.Current
            : StringComparer.Ordinal;

    public static bool PathsEqual(IFilePanelSource source, string left, string right) =>
        Comparer(source).Equals(source.NormalizePath(left), source.NormalizePath(right));

    public static bool IsDescendantOf(IFilePanelSource source, string path, string possibleAncestor)
    {
        string ancestor = source.NormalizePath(possibleAncestor);
        string? current = NormalizeParent(source, path);
        if (current is null)
            return false;

        var visited = new HashSet<string>(Comparer(source));
        while (current is not null && visited.Add(current))
        {
            if (Comparer(source).Equals(current, ancestor))
                return true;

            if (source.IsRootPath(current))
                return false;

            current = NormalizeParent(source, current);
        }

        return false;
    }

    public static bool IsSameOrDescendant(IFilePanelSource source, string path, string possibleAncestor) =>
        PathsEqual(source, path, possibleAncestor) || IsDescendantOf(source, path, possibleAncestor);

    private static string? NormalizeParent(IFilePanelSource source, string path)
    {
        string normalized = source.NormalizePath(path);
        string? parent = source.GetParentPath(normalized);
        if (parent is null)
            return null;

        string normalizedParent = source.NormalizePath(parent);
        return Comparer(source).Equals(normalizedParent, normalized)
            ? null
            : normalizedParent;
    }
}
