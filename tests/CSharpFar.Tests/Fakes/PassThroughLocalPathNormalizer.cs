using CSharpFar.Core.Abstractions;

namespace CSharpFar.Tests.Fakes;

internal sealed class PassThroughLocalPathNormalizer : ILocalPathNormalizer
{
    public static PassThroughLocalPathNormalizer Instance { get; } = new();

    private PassThroughLocalPathNormalizer()
    {
    }

    public string Normalize(string path, string? basePath = null) => path;

    public bool TryNormalize(
        string path,
        string? basePath,
        out string normalizedPath)
    {
        normalizedPath = path;
        return true;
    }
}
