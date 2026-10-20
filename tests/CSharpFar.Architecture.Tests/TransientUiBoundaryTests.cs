namespace CSharpFar.Architecture.Tests;

public sealed class TransientUiBoundaryTests
{
    private static readonly string[] ForbiddenPopupPlumbing =
    [
        "IUiCanvas",
        "PopupRenderer",
        "DialogFrameRenderer",
        "RoutedScrollableList",
        "UiInteractionFrameBuilder",
        "AddHitRegion",
        "BuildInteractionFrame",
        "RenderFrame",
        "ScrollBarInteraction",
        "VerticalScrollbar",
    ];

    [Theory]
    [InlineData("PanelQuickSearchLayer.cs")]
    [InlineData("CommandCompletionLayer.cs")]
    public void MigratedTransientFeatures_DoNotOwnPopupPresentationPlumbing(string fileName)
    {
        string root = RepositoryPaths.FindRoot();
        string file = Path.Combine(root, "src", "CSharpFar.App", "Rendering", fileName);
        string source = File.ReadAllText(file);

        string[] violations = ForbiddenPopupPlumbing
            .Where(source.Contains)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{fileName} must describe transient UI semantics and leave generic presentation/routing to CSharpFar.Ui: "
            + string.Join(", ", violations));
    }

}
