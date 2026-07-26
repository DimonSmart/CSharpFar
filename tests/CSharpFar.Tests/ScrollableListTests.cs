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
        Assert.Null(list.GetScrollbarDrag());
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
        Assert.NotNull(list.GetScrollbarDrag());
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
        Assert.Null(list.GetScrollbarDrag());
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
        ScrollBarDragState before = Assert.IsType<ScrollBarDragState>(list.GetScrollbarDrag());

        ScrollableListFrameState frame = list.CalculateFrameState(4, new Rect(9, 2, 1, 4));

        Assert.Equal(before, list.GetScrollbarDrag());
        Assert.NotNull(frame.VerticalScrollbarFrame?.DragState);
        Assert.Equal(new Rect(9, 2, 1, 4), frame.VerticalScrollbarFrame?.DragState!.Value.Bounds);
        Assert.Equal(4, frame.VerticalScrollbarFrame?.DragState.Value.ViewportItems);
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

        Assert.Equal(new Rect(9, 2, 1, 4), list.GetScrollbarDrag()!.Value.Bounds);
        Assert.Equal(4, Assert.IsType<ScrollBarDragState>(list.GetScrollbarDrag()).ViewportItems);
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

        Assert.Null(list.GetScrollbarDrag());
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
        Assert.Null(list.GetScrollbarDrag());
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

    [Fact]
    public void RoutedList_BuildsTargetsAndTranslatesScrollbarDragToMouseCapture()
    {
        var list = Create(Enumerable.Range(0, 20).Select(i => i.ToString()).ToArray());
        var listTarget = new UiTargetId("test.list");
        var scrollbarTarget = new UiTargetId("test.list.scrollbar");
        var routed = new RoutedScrollableList<string>(list, listTarget, scrollbarTarget);
        Rect contentBounds = new(0, 0, 9, 6);
        ScrollableListFrameState frame = routed.CalculateFrame(6, new Rect(9, 0, 1, 6));

        UiInteractionFragment fragment = routed.BuildInteractionFragment(contentBounds, frame, 2);
        Assert.Equal([listTarget, scrollbarTarget], fragment.HitRegions.Select(region => region.Target));
        Assert.Equal(listTarget, Assert.Single(fragment.FocusEntries).Target);

        var focus = new UiFocusController();
        RoutedScrollableListInputResult started = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            contentBounds,
            frame,
            UiInputRouteContext.HitTarget(focus, scrollbarTarget));

        Assert.True(started.ListResult.DragStarted);
        Assert.Equal(UiFocusRequestKind.Set, started.UiResult.FocusRequest.Kind);
        Assert.Equal(listTarget, started.UiResult.FocusRequest.Target);
        Assert.Equal(UiMouseCaptureRequestKind.Capture, started.UiResult.MouseCaptureRequest.Kind);
        Assert.Equal(scrollbarTarget, started.UiResult.MouseCaptureRequest.Target);

        frame = routed.CalculateFrame(6, new Rect(9, 0, 1, 6));
        RoutedScrollableListInputResult ended = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Up, 20, 3),
            contentBounds,
            frame,
            UiInputRouteContext.CapturedTarget(focus, scrollbarTarget));

        Assert.True(ended.ListResult.DragEnded);
        Assert.Equal(UiMouseCaptureRequestKind.Release, ended.UiResult.MouseCaptureRequest.Kind);
    }

    [Fact]
    public void RoutedList_ForwardsOrdinaryListStateAndOperations()
    {
        var routed = new RoutedScrollableList<string>(
            Create(["a", "b"]),
            new UiTargetId("test.list"),
            new UiTargetId("test.list.scrollbar"));
        var changes = new List<int>();
        routed.SelectionChanged = (_, index) => changes.Add(index);

        routed.SelectedIndex = 1;
        routed.ScrollTop = 1;
        routed.EmptyText = "Empty";
        routed.ReplaceItems(["b", "c"], static item => item, viewportRows: 1);
        routed.ResetItems(["x", "y"], selectedIndex: 1);

        Assert.Equal(["x", "y"], routed.Items);
        Assert.Equal(2, routed.Count);
        Assert.True(routed.HasItems);
        Assert.Equal("y", routed.SelectedItemOrDefault);
        Assert.Equal("Empty", routed.EmptyText);
        Assert.NotNull(routed.GetScrollState(1));

        RoutedScrollableListInputResult result = routed.RouteInput(
            new KeyConsoleInputEvent(Key(ConsoleKey.UpArrow)),
            new Rect(0, 0, 5, 1),
            routed.CalculateFrame(1, null),
            UiInputRouteContext.KeyboardTarget(new UiFocusController(), routed.ListTarget));

        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, result.ListResult.Kind);
        Assert.Equal([0], changes);
    }

    [Fact]
    public void RoutedList_NonFocusablePolicy_PublishesMouseTargetsAndAcceptsOwnerKeyboardRoute()
    {
        var list = Create(Enumerable.Range(0, 20).Select(index => index.ToString()).ToArray());
        var routed = new RoutedScrollableList<string>(
            list,
            new UiTargetId("test.list"),
            new UiTargetId("test.list.scrollbar"),
            new RoutedScrollableListInteractionOptions { AcceptKeyboardFromLayerRoute = true });
        Rect contentBounds = new(0, 0, 9, 6);
        ScrollableListFrameState frame = routed.CalculateFrame(6, new Rect(9, 0, 1, 6));
        var focus = new UiFocusController();

        UiInteractionFragment fragment = routed.BuildInteractionFragment(contentBounds, frame, 0);
        Assert.Equal([routed.ListTarget, routed.ScrollbarTarget], fragment.HitRegions.Select(region => region.Target));
        Assert.Empty(fragment.FocusEntries);

        RoutedScrollableListInputResult keyboard = routed.RouteInput(
            new KeyConsoleInputEvent(Key(ConsoleKey.DownArrow)),
            contentBounds,
            frame,
            UiInputRouteContext.Layer(focus));
        Assert.Equal(ScrollableListInputResultKind.SelectionChanged, keyboard.ListResult.Kind);

        RoutedScrollableListInputResult wheel = routed.RouteInput(
            Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 1, 1),
            contentBounds,
            frame,
            UiInputRouteContext.HitTarget(focus, routed.ListTarget));
        Assert.True(wheel.ListResult.IsHandled);
        Assert.Equal(UiFocusRequestKind.None, wheel.UiResult.FocusRequest.Kind);

        RoutedScrollableListInputResult started = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            contentBounds,
            frame,
            UiInputRouteContext.HitTarget(focus, routed.ScrollbarTarget));
        Assert.True(started.ListResult.DragStarted);
        Assert.Equal(UiMouseCaptureRequestKind.Capture, started.UiResult.MouseCaptureRequest.Kind);
        Assert.Equal(UiFocusRequestKind.None, started.UiResult.FocusRequest.Kind);

        frame = routed.CalculateFrame(6, new Rect(9, 0, 1, 6));
        RoutedScrollableListInputResult ended = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Up, 20, 3),
            contentBounds,
            frame,
            UiInputRouteContext.CapturedTarget(focus, routed.ScrollbarTarget));
        Assert.True(ended.ListResult.DragEnded);
        Assert.Equal(UiMouseCaptureRequestKind.Release, ended.UiResult.MouseCaptureRequest.Kind);
    }

    private static ScrollableList<string> Create(IReadOnlyList<string> items) => new(items, static item => item);

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x = 0, int y = 0) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
