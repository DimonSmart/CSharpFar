using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Services;

public sealed class WindowsPanelPathSemantics : IPanelPathSemantics
{
    public bool IsRoot(string path)
    {
        string trimmed = TrimTrailingSeparators(path);
        if (IsDriveRoot(trimmed))
            return true;

        return IsUncShareRoot(trimmed);
    }

    public string? GetParentPath(string path)
    {
        string trimmed = TrimTrailingSeparators(path);
        if (trimmed.Length == 0 || IsRoot(trimmed))
            return null;

        int index = trimmed.LastIndexOf('\\');
        if (index < 0)
            return null;

        if (index == 2 && IsDriveRoot(trimmed[..2]))
            return trimmed[..3];

        string parent = trimmed[..index];
        return parent.Length == 2 && IsDriveRoot(parent)
            ? parent + "\\"
            : parent;
    }

    public string GetFileName(string path)
    {
        string trimmed = TrimTrailingSeparators(path);
        if (trimmed.Length == 0)
            return string.Empty;

        int index = trimmed.LastIndexOf('\\');
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    public string TrimTrailingSeparators(string path)
    {
        string normalized = NormalizeSeparators(path);
        if (normalized.Length == 0)
            return normalized;

        while (normalized.Length > 0 &&
               IsSeparator(normalized[^1]) &&
               !ShouldKeepTrailingSeparator(normalized))
        {
            normalized = normalized[..^1];
        }

        return normalized;
    }

    private static string NormalizeSeparators(string path) => path.Replace('/', '\\');

    private static bool IsSeparator(char value) => value is '\\' or '/';

    private static bool IsDriveRoot(string path) =>
        (path.Length == 2 &&
         char.IsLetter(path[0]) &&
         path[1] == ':') ||
        (path.Length == 3 &&
         char.IsLetter(path[0]) &&
         path[1] == ':' &&
         path[2] == '\\');

    private static bool ShouldKeepTrailingSeparator(string path) =>
        path.Length == 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        path[2] == '\\';

    private static bool IsUncShareRoot(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;

        string[] parts = path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2;
    }
}
