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
        string root = FindRepositoryRoot();
        string[] path = [root, "src", "CSharpFar.App", .. relativePath];
        return File.ReadAllText(Path.Combine(path));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "CSharpFar.App")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
