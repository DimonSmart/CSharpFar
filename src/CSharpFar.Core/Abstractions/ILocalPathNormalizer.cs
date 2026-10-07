namespace CSharpFar.Core.Abstractions;

public interface ILocalPathNormalizer
{
    string Normalize(string path, string? basePath = null);

    bool TryNormalize(
        string path,
        string? basePath,
        out string normalizedPath);
}
