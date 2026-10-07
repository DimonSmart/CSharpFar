using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;

namespace CSharpFar.Tests.Fakes;

internal static class TestPanelControllerFactory
{
    public static PanelController Create(
        IPanelViewBuilder viewBuilder,
        IPanelPathSemantics? pathSemantics = null) =>
        new(
            viewBuilder,
            pathSemantics,
            PassThroughLocalPathNormalizer.Instance);
}

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
