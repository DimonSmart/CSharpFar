namespace CSharpFar.Architecture.Tests;

public sealed class ApplicationPointerRoutingBoundaryTests
{
    [Fact]
    public void FunctionKeyRenderer_UsesControllerGeometryInsteadOfLowLevelSlots()
    {
        string source = ReadAppSource("Rendering", "ApplicationFunctionKeyBarRenderer.cs");

        Assert.DoesNotContain("FunctionKeyBar.BuildSlots", source, StringComparison.Ordinal);
        Assert.Contains("BuildActionHits", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationUiSurface_DoesNotReprojectRoutedPointerItems()
    {
        string source = ReadAppSource("Rendering", "ApplicationUiSurface.cs");

        Assert.DoesNotContain("RoutedPointerItem<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new RoutedPointerCollection<", source, StringComparison.Ordinal);
    }

    private static string ReadAppSource(params string[] relativePath)
    {
        string root = RepositoryPaths.FindRoot();
        string[] path = [root, "src", "CSharpFar.App", .. relativePath];
        return File.ReadAllText(Path.Combine(path));
    }

}
