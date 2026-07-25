using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class UiLayoutTests
{
    [Theory]
    [InlineData(80, 25, 20, 10, 30, 7, 20, 10)]
    [InlineData(9, 7, 4, 2, 2, 2, 4, 2)]
    [InlineData(5, 4, 9, 8, 0, 0, 5, 4)]
    [InlineData(0, 0, 3, 2, 0, 0, 0, 0)]
    public void Center_ConstrainsRequestedSizeAndNeverProducesNegativeBounds(int viewportWidth, int viewportHeight, int width, int height, int x, int y, int expectedWidth, int expectedHeight)
    {
        Rect actual = UiLayout.Center(new ConsoleSize(viewportWidth, viewportHeight), width, height);

        Assert.Equal(new Rect(x, y, expectedWidth, expectedHeight), actual);
        Assert.True(actual.X >= 0 && actual.Y >= 0 && actual.Width >= 0 && actual.Height >= 0);
    }

    [Fact]
    public void Inset_HandlesSymmetricAsymmetricAndOverlappingInsets()
    {
        Rect bounds = new(10, 20, 8, 6);

        Assert.Equal(new Rect(11, 21, 6, 4), UiLayout.Inset(bounds, 1, 1));
        Assert.Equal(new Rect(11, 22, 3, 1), UiLayout.Inset(bounds, 1, 2, 4, 3));
        Rect empty = UiLayout.Inset(new Rect(0, 0, 2, 2), 2, 1, 2, 2);
        Assert.Equal(new Rect(2, 1, 0, 0), empty);
    }

    [Theory]
    [InlineData(10, 20, 2, 2, 10, 0, 0, 0)]
    [InlineData(10, 20, 2, 2, 0, 10, 0, 0)]
    [InlineData(10, 20, 2, 2, 1, 0, 10, 0)]
    [InlineData(10, 20, 2, 2, 0, 1, 0, 10)]
    public void Inset_OversizedInsetsRemainContained(int x, int y, int width, int height, int left, int top, int right, int bottom)
    {
        Rect bounds = new(x, y, width, height);
        Rect result = UiLayout.Inset(bounds, left, top, right, bottom);

        Assert.InRange(result.X, bounds.X, bounds.Right);
        Assert.InRange(result.Y, bounds.Y, bounds.Bottom);
        Assert.True(result.Right <= bounds.Right);
        Assert.True(result.Bottom <= bounds.Bottom);
        Assert.True(result.Width >= 0 && result.Height >= 0);
    }

    [Fact]
    public void SplitBottom_SeparatesBodyFooterAndGapWithoutOverlap()
    {
        Rect bounds = new(3, 5, 10, 8);

        VerticalLayoutSplit split = UiLayout.SplitBottom(bounds, footerHeight: 2, gap: 1);

        Assert.Equal(new Rect(3, 5, 10, 5), split.Body);
        Assert.Equal(new Rect(3, 11, 10, 2), split.Footer);
        Assert.True(split.Body.Bottom <= split.Footer.Y);
        Assert.Equal(new Rect(3, 5, 10, 0), UiLayout.SplitBottom(bounds, footerHeight: 8).Body);
        Assert.Equal(bounds, UiLayout.SplitBottom(bounds, footerHeight: 20).Footer);
    }

    [Theory]
    [InlineData(5, 0, 3, 0)]
    [InlineData(5, 10, 2, 5)]
    [InlineData(5, 2, 10, 2)]
    [InlineData(5, -1, -1, 0)]
    public void SplitBottom_ContainsBothSectionsForBoundaryInputs(int height, int footerHeight, int gap, int expectedFooterHeight)
    {
        Rect bounds = new(3, 5, 10, height);
        VerticalLayoutSplit split = UiLayout.SplitBottom(bounds, footerHeight, gap);

        Assert.Equal(expectedFooterHeight, split.Footer.Height);
        Assert.True(split.Body.X >= bounds.X && split.Body.Bottom <= bounds.Bottom);
        Assert.True(split.Footer.X >= bounds.X && split.Footer.Bottom <= bounds.Bottom);
        Assert.Equal(bounds.Width, split.Body.Width);
        Assert.Equal(bounds.Width, split.Footer.Width);
        Assert.True(split.Body.Bottom <= split.Footer.Y);
    }

    [Theory]
    [InlineData(80, 25, 50, 14)]
    [InlineData(10, 25, 10, 14)]
    [InlineData(80, 5, 50, 5)]
    [InlineData(2, 2, 2, 2)]
    public void ModalDialogRenderer_RenderUsesCalculatedLayout(int width, int height, int expectedWidth, int expectedHeight)
    {
        var renderer = new ModalDialogRenderer();
        ModalDialogRenderer.Layout expected = renderer.CalculateLayout(new ConsoleSize(width, height), 50, 14);
        ModalDialogRenderer.Layout? rendered = null;
        var screen = new ScreenRenderer(new FakeConsoleDriver(width, height));

        UiTestRender.Render(screen, canvas => renderer.Render(
            canvas,
            expected.OuterBounds,
            "Test",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(PaletteRegistry.Default),
            PaletteStyles.DialogPopupOptions(PaletteRegistry.Default),
            (_, layout) => rendered = layout));

        Assert.Equal(expected, rendered);
        Assert.Equal(expectedWidth, expected.OuterBounds.Width);
        Assert.Equal(expectedHeight, expected.OuterBounds.Height);
    }

    [Fact]
    public void ModalDialogRenderer_RenderWithLayoutPassesTheExactProvidedLayout()
    {
        var renderer = new ModalDialogRenderer();
        ModalDialogRenderer.Layout layout = renderer.CalculateLayout(new ConsoleSize(80, 25), 50, 14);
        ModalDialogRenderer.Layout? rendered = null;
        var screen = new ScreenRenderer(new FakeConsoleDriver(80, 25));

        UiTestRender.Render(screen, canvas => renderer.Render(
            canvas, layout, "Test", true,
            PaletteStyles.DialogPopupOptions(PaletteRegistry.Default),
            PaletteStyles.DialogPopupOptions(PaletteRegistry.Default),
            (_, actual) => rendered = actual));

        Assert.Equal(layout, rendered);
    }

    [Fact]
    public void UiTargetScope_CreatesStableRelatedIdsAndValidatesSegments()
    {
        var scope = new UiTargetScope("dialog");

        Assert.Equal("dialog", scope.Root.Value);
        Assert.Equal("dialog.list", scope.Child("list").Value);
        Assert.Equal("dialog.list.scrollbar", scope.Child("list.scrollbar").Value);
        Assert.Throws<ArgumentException>(() => new UiTargetScope(" "));
        Assert.Throws<ArgumentException>(() => scope.Child(" "));
    }
}
