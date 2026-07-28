using CSharpFar.Core.Abstractions;

namespace CSharpFar.FileSystem;

internal static class ProviderPathRelations
{
    public static bool PathsEqual(IFilePanelSource source, string left, string right) =>
        string.Equals(source.NormalizePath(left), source.NormalizePath(right), StringComparison.Ordinal);

    public static bool IsDescendantOf(IFilePanelSource source, string path, string possibleAncestor)
    {
        string ancestor = source.NormalizePath(possibleAncestor);
        string? current = NormalizeParent(source, path);
        if (current is null)
            return false;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && visited.Add(current))
        {
            if (string.Equals(current, ancestor, StringComparison.Ordinal))
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
        return string.Equals(normalizedParent, normalized, StringComparison.Ordinal)
            ? null
            : normalizedParent;
    }
}
