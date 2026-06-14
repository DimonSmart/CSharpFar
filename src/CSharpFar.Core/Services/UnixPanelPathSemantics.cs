using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Services;

public sealed class UnixPanelPathSemantics : IPanelPathSemantics
{
    public bool IsRoot(string path) => TrimTrailingSeparators(path) == "/";

    public string? GetParentPath(string path)
    {
        string trimmed = TrimTrailingSeparators(path);
        if (trimmed.Length == 0 || IsRoot(trimmed))
            return null;

        int index = trimmed.LastIndexOf('/');
        if (index < 0)
            return null;

        return index == 0 ? "/" : trimmed[..index];
    }

    public string GetFileName(string path)
    {
        string trimmed = TrimTrailingSeparators(path);
        if (trimmed.Length == 0 || trimmed == "/")
            return string.Empty;

        int index = trimmed.LastIndexOf('/');
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    public string TrimTrailingSeparators(string path)
    {
        if (path.Length == 0)
            return path;

        string trimmed = path;
        while (trimmed.Length > 1 && trimmed[^1] == '/')
            trimmed = trimmed[..^1];

        return trimmed;
    }
}
