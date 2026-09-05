using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class ScrollableListStateTests
{
    [Fact]
    public void State_OwnsSnapshotsAcrossAllItemReplacementPaths()
    {
        var initial = new List<string> { "one", "two" };
        var state = new ScrollableListState<string>(initial, 1);
        initial.Clear();
        Assert.Equal(["one", "two"], state.Items);

        var reset = new List<string> { "three", "four" };
        state.ResetItems(reset, 1);
        reset.Clear();
        Assert.Equal(["three", "four"], state.Items);

        var replacement = new List<string> { "five", "six" };
        state.ReplaceItems(replacement, static value => value, 1);
        replacement.Clear();
        Assert.Equal(["five", "six"], state.Items);
    }

    [Fact]
    public void Selection_RejectsInvalidIndicesAndHandlesReferenceAndValueItems()
    {
        var text = new ScrollableListState<string>(["one"]);
        Assert.True(text.TryGetSelectedItem(out string selected));
        Assert.Equal("one", selected);
        Assert.False(text.TrySetSelectedIndex(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => text.SetSelectedIndex(-1, 1));

        var numbers = new ScrollableListState<int>([42]);
        Assert.True(numbers.TryGetSelectedItem(out int number));
        Assert.Equal(42, number);

        var empty = new ScrollableListState<int>([]);
        Assert.False(empty.TryGetSelectedItem(out _));
        Assert.Equal(-1, empty.SelectedIndex);
        Assert.Equal(0, empty.ScrollTop);
    }

    [Fact]
    public void ReplaceItems_PreservesIdentityOrNearestValidIndex()
    {
        var state = new ScrollableListState<int>([1, 2, 3], 2);
        state.ReplaceItems([0, 3, 4], static value => value, 1);
        Assert.Equal(1, state.SelectedIndex);
        Assert.True(state.TryGetSelectedItem(out int selected));
        Assert.Equal(3, selected);

        state.ReplaceItems([7], static value => value, 1);
        Assert.Equal(0, state.SelectedIndex);
        Assert.True(state.TryGetSelectedItem(out selected));
        Assert.Equal(7, selected);
    }
}
