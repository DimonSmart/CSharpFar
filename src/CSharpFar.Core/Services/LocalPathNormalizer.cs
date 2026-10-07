using CSharpFar.Core.Abstractions;

namespace CSharpFar.Core.Services;

public sealed class LocalPathNormalizer : ILocalPathNormalizer
{
    public static ILocalPathNormalizer Current { get; } = new LocalPathNormalizer();

    private readonly IPanelPathSemantics _pathSemantics;

    public LocalPathNormalizer()
        : this(PanelPathSemantics.Current)
    {
    }

    internal LocalPathNormalizer(IPanelPathSemantics pathSemantics)
    {
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    public string Normalize(string path, string? basePath = null)
    {
        string fullPath = basePath is null
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, basePath);

        return _pathSemantics.TrimTrailingSeparators(fullPath);
    }

    public bool TryNormalize(
        string path,
        string? basePath,
        out string normalizedPath)
    {
        try
        {
            normalizedPath = Normalize(path, basePath);
            return true;
        }
        catch (Exception ex) when (IsNormalizationException(ex))
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static bool IsNormalizationException(Exception exception) =>
        exception is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
}
