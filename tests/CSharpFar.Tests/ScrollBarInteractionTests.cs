using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ScrollBarInteractionTests
{
    [Fact]
    public void RebaseDrag_WithUnchangedGeometryReturnsEquivalentState()
    {
        var drag = new ScrollBarDragState(new Rect(4, 2, 1, 8), 20, 6, 1);

        Assert.Equal(drag, ScrollBarInteraction.RebaseDrag(
            drag,
            drag.Bounds,
            drag.TotalItems,
            drag.ViewportItems));
    }

    [Fact]
    public void RebaseDrag_UpdatesGeometryDimensionsAndClampsPointerOffset()
    {
        var drag = new ScrollBarDragState(new Rect(4, 2, 1, 8), 20, 6, 9);
        var bounds = new Rect(9, 3, 1, 5);

        ScrollBarDragState rebased = Assert.IsType<ScrollBarDragState>(
            ScrollBarInteraction.RebaseDrag(drag, bounds, totalItems: 12, viewportItems: 4));

        Assert.Equal(bounds, rebased.Bounds);
        Assert.Equal(12, rebased.TotalItems);
        Assert.Equal(4, rebased.ViewportItems);
        int thumbHeight = ScrollBarInteraction.CalculateThumb(
            bounds,
            new ScrollState { TotalItems = 12, ViewportItems = 4, FirstVisibleIndex = 0 }).ThumbHeight;
        Assert.Equal(thumbHeight - 1, rebased.PointerOffsetInThumb);
    }

    [Fact]
    public void RebaseDrag_ClampsNegativePointerOffsetToZero()
    {
        var drag = new ScrollBarDragState(new Rect(0, 0, 1, 8), 20, 6, -4);

        ScrollBarDragState rebased = Assert.IsType<ScrollBarDragState>(
            ScrollBarInteraction.RebaseDrag(drag, new Rect(3, 2, 1, 8), 20, 6));

        Assert.Equal(0, rebased.PointerOffsetInThumb);
    }

    [Theory]
    [InlineData(2, 12, 1)]
    [InlineData(6, 4, 4)]
    [InlineData(6, 4, 0)]
    public void RebaseDrag_ReturnsNullWhenScrollbarIsNotInteractive(
        int height,
        int totalItems,
        int viewportItems)
    {
        var drag = new ScrollBarDragState(new Rect(0, 0, 1, 6), 12, 4, 0);

        Assert.Null(ScrollBarInteraction.RebaseDrag(
            drag,
            new Rect(0, 0, 1, height),
            totalItems,
            viewportItems));
    }
}
