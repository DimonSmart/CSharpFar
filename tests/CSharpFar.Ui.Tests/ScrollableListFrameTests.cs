using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ScrollableListFrameTests
{
    [Fact]
    public void Calculate_NormalizesOnlyTheFrameAndDoesNotMutateState()
    {
        var state = new ScrollableListState<string>(["one", "two", "three"], 2);
        var frame = ScrollableListFrame.Calculate(state, new Rect(0, 0, 10, 1), new Rect(10, 0, 1, 1), new VerticalScrollbarController());

        Assert.Equal(0, state.ScrollTop);
        Assert.Equal(2, frame.SelectedIndex);
        Assert.Equal(2, frame.ScrollTop);
    }

    [Fact]
    public void FromCommitted_HandlesEmptyAndCollapsedBoundsWithoutInvalidGeometry()
    {
        ScrollableListFrame empty = ScrollableListFrame.FromCommitted(new Rect(0, 0, 0, 0), 0, 0, 8, 0);
        ScrollableListFrame populated = ScrollableListFrame.FromCommitted(new Rect(0, 0, 0, 0), 3, 8, 8, 0);

        Assert.Equal(-1, empty.SelectedIndex);
        Assert.Equal(0, empty.ScrollTop);
        Assert.Equal(2, populated.SelectedIndex);
        Assert.Equal(2, populated.ScrollTop);
    }

    [Fact]
    public void Calculate_PublishesScrollbarOnlyWhenContentOverflows()
    {
        var scrollbar = new VerticalScrollbarController();

        Assert.Null(ScrollableListFrame.Calculate(new ScrollableListState<int>([1, 2, 3]), new Rect(0, 0, 5, 3), new Rect(5, 0, 1, 3), scrollbar).Scrollbar);
        Assert.NotNull(ScrollableListFrame.Calculate(new ScrollableListState<int>([1, 2, 3, 4]), new Rect(0, 0, 5, 3), new Rect(5, 0, 1, 3), scrollbar).Scrollbar);
    }
}
