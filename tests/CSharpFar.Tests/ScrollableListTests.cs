using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ScrollableListTests
{
    [Fact]
    public void ResetItems_ReplacesItemsResetsViewportSelectionAndDrag()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        list.SelectedIndex = 10;
        list.ScrollTop = 8;
        list.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1), new Rect(0, 0, 9, 6), new Rect(9, 0, 1, 6), 6);

        list.ResetItems(["a", "b"], 1);

        Assert.Equal(["a", "b"], list.Items);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(0, list.ScrollTop);
        Assert.Null(list.ScrollbarDrag);
        list.ResetItems([]);
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void Normalize_EmptyList_SetsSelectedIndexToMinusOneAndScrollTopToZero()
    {
        var list = Create([]);
        list.SelectedIndex = 4;
        list.ScrollTop = 3;

        list.Normalize(2);

        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, list.ScrollTop);
    }

    [Fact]
    public void Normalize_ClampsSelectedIndexToValidRange()
    {
        var list = Create(["a", "b"]);
        list.SelectedIndex = 10;

        list.Normalize(2);

        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void Normalize_EnsuresSelectedItemVisible()
    {
        var list = Create(["0", "1", "2", "3", "4"]);
        list.SelectedIndex = 4;

        list.Normalize(2);

        Assert.Equal(3, list.ScrollTop);
    }

    [Fact]
    public void HandleKey_UpDown_ChangesSelection()
    {
        var list = Create(["a", "b", "c"]);

        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, list.HandleKey(Key(ConsoleKey.DownArrow), 2).Kind);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, list.HandleKey(Key(ConsoleKey.UpArrow), 2).Kind);
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void HandleKey_SelectionChange_NotifiesOnlyWhenIndexChanges()
    {
        var list = Create(["a", "b"]);
        var changes = new List<int>();
        list.SelectionChanged = (_, index) => changes.Add(index);

        list.HandleKey(Key(ConsoleKey.UpArrow), 2);
        list.HandleKey(Key(ConsoleKey.DownArrow), 2);

        Assert.Equal([1], changes);
    }

    [Fact]
    public void HandleKey_PageUpPageDown_MovesByViewportRows()
    {
        var list = Create(Enumerable.Range(0, 10).Select(i => i.ToString()).ToArray());

        list.HandleKey(Key(ConsoleKey.PageDown), 3);
        Assert.Equal(3, list.SelectedIndex);
        list.HandleKey(Key(ConsoleKey.PageUp), 3);
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void HandleKey_HomeEnd_MovesToFirstAndLast()
    {
        var list = Create(["a", "b", "c"]);

        list.HandleKey(Key(ConsoleKey.End), 2);
        Assert.Equal(2, list.SelectedIndex);
        list.HandleKey(Key(ConsoleKey.Home), 2);
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void HandleKey_Enter_ReturnsConfirmedWhenListHasItems()
    {
        Assert.Equal(
            ScrollableListInputResultKind.Confirmed,
            Create(["a"]).HandleKey(Key(ConsoleKey.Enter), 1).Kind);
    }

    [Fact]
    public void HandleKey_Enter_DoesNotConfirmEmptyList()
    {
        Assert.Equal(
            ScrollableListInputResultKind.Handled,
            Create([]).HandleKey(Key(ConsoleKey.Enter), 1).Kind);
    }

    [Fact]
    public void HandleMouse_WheelInsideContent_ChangesSelectionAndKeepsVisible()
    {
        var list = Create(["0", "1", "2", "3"]);
        ScrollBarDragState? drag = null;

        var firstResult = list.HandleMouse(Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 2, 1), new Rect(0, 0, 5, 2), null, 2, ref drag);
        list.HandleMouse(Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 2, 1), new Rect(0, 0, 5, 2), null, 2, ref drag);

        Assert.True(firstResult.IsHandled);
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal(1, list.ScrollTop);
        list.HandleMouse(Mouse(MouseButton.WheelUp, MouseEventKind.Wheel, 2, 1), new Rect(0, 0, 5, 2), null, 2, ref drag);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void HandleMouse_WheelOutsideContentAndScrollbar_ReturnsNotHandled()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        var changes = new List<int>();
        list.SelectionChanged = (_, index) => changes.Add(index);
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(
            Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 12, 8),
            new Rect(0, 0, 9, 5),
            new Rect(9, 0, 1, 5),
            5,
            ref drag);

        Assert.Equal(ScrollableListInputResultKind.NotHandled, result.Kind);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, list.ScrollTop);
        Assert.Empty(changes);
    }

    [Fact]
    public void HandleMouse_WheelInsideScrollbar_HandlesScroll()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(
            Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 9, 2),
            new Rect(0, 0, 9, 5),
            new Rect(9, 0, 1, 5),
            5,
            ref drag);

        Assert.True(result.IsHandled);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void HandleMouse_ClickInside_SelectsItem()
    {
        var list = Create(["a", "b", "c"]);
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down, 2, 3), new Rect(1, 2, 5, 3), null, 3, ref drag);

        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, result.Kind);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void HandleMouse_ClickInside_ReturnsConfirmedWhenConfirmOnClickEnabled()
    {
        var list = Create(["a"]);
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down), new Rect(0, 0, 5, 1), null, 1, ref drag, confirmOnMouseDown: true);

        Assert.Equal(ScrollableListInputResultKind.Confirmed, result.Kind);
    }

    [Fact]
    public void HandleMouse_DoubleClick_ReturnsConfirmedWhenConfirmOnDoubleClickEnabled()
    {
        var list = Create(["a"]);
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.DoubleClick), new Rect(0, 0, 5, 1), null, 1, ref drag);

        Assert.Equal(ScrollableListInputResultKind.Confirmed, result.Kind);
    }

    [Fact]
    public void HandleMouse_ClickOutside_ReturnsNotHandled()
    {
        var list = Create(["a"]);
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down, 8, 8), new Rect(0, 0, 5, 1), null, 1, ref drag);

        Assert.Equal(ScrollableListInputResultKind.NotHandled, result.Kind);
    }

    [Fact]
    public void HandleMouse_ScrollbarSynchronizesSelectionAndScrollTop()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        ScrollBarDragState? drag = null;

        var result = list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 5),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6,
            ref drag);

        Assert.True(result.IsHandled);
        Assert.True(list.ScrollTop > 0);
        Assert.InRange(list.SelectedIndex, list.ScrollTop, list.ScrollTop + 5);
    }

    [Fact]
    public void HandleMouse_ThumbDownCreatesDrag()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());

        var result = list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);

        Assert.True(result.DragStarted);
        Assert.NotNull(list.ScrollbarDrag);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(4)]
    public void HandleMouse_ScrollbarClickOutsideThumbDoesNotCreateDrag(int y)
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());

        var result = list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, y),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);

        Assert.False(result.DragStarted);
        Assert.Null(list.ScrollbarDrag);
    }

    [Fact]
    public void CalculateFrameState_RebasesCommittedDragWithoutMutatingIt()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);
        ScrollBarDragState before = Assert.IsType<ScrollBarDragState>(list.ScrollbarDrag);

        ScrollableListFrameState frame = list.CalculateFrameState(4, new Rect(9, 2, 1, 4));

        Assert.Equal(before, list.ScrollbarDrag);
        Assert.NotNull(frame.ScrollbarDrag);
        Assert.Equal(new Rect(9, 2, 1, 4), frame.ScrollbarDrag!.Value.Bounds);
        Assert.Equal(4, frame.ScrollbarDrag.Value.ViewportItems);
    }

    [Fact]
    public void ApplyCommittedFrame_ReplacesCommittedDrag()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);

        ScrollableListFrameState frame = list.CalculateFrameState(4, new Rect(9, 2, 1, 4));
        list.ApplyCommittedFrame(frame);

        Assert.Equal(new Rect(9, 2, 1, 4), list.ScrollbarDrag!.Value.Bounds);
        Assert.Equal(4, list.ScrollbarDrag.Value.ViewportItems);
    }

    [Fact]
    public void ApplyCommittedFrame_ClearsDragWhenScrollbarDisappears()
    {
        var list = Create(Enumerable.Range(0, 5).Select(i => i.ToString()).ToArray());
        list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            new Rect(0, 0, 9, 3),
            new Rect(9, 0, 1, 3),
            3);

        list.ApplyCommittedFrame(list.CalculateFrameState(5, scrollbarBounds: null));

        Assert.Null(list.ScrollbarDrag);
    }

    [Fact]
    public void HandleMouse_MouseUpEndsDrag()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);

        var result = list.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Up, 9, 1),
            new Rect(0, 0, 9, 6),
            new Rect(9, 0, 1, 6),
            6);

        Assert.True(result.DragEnded);
        Assert.Null(list.ScrollbarDrag);
    }

    [Fact]
    public void GetScrollState_ReturnsNullWhenListFitsViewport()
    {
        Assert.Null(Create(["a", "b"]).GetScrollState(2));
    }

    [Fact]
    public void GetScrollState_ReturnsStateWhenListIsScrollable()
    {
        var state = Create(["a", "b", "c"]).GetScrollState(2);

        Assert.NotNull(state);
        Assert.Equal(3, state.TotalItems);
        Assert.Equal(2, state.ViewportItems);
    }

    [Fact]
    public void Render_EmptyList_DrawsEmptyText()
    {
        var driver = new FakeConsoleDriver(10, 3);
        var list = Create([]);
        list.EmptyText = "Empty";

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            list.Render(canvas, new Rect(0, 0, 10, 2)));

        Assert.StartsWith("Empty", driver.GetRow(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SelectedItem_UsesSelectedStyle()
    {
        var driver = new FakeConsoleDriver(10, 3);
        var list = Create(["a", "b"]);
        list.SelectedIndex = 1;
        list.NormalStyle = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        list.SelectedStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            list.Render(canvas, new Rect(0, 0, 10, 2)));

        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(0, 1).Foreground);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(0, 1).Background);
    }

    private static ScrollableList<string> Create(IReadOnlyList<string> items) => new(items, static item => item);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x = 0, int y = 0) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
