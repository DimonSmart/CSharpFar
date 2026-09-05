using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class RoutedScrollableViewportTests
{
    [Fact]
    public void BuildInteractionFragment_UsesOnlyCommittedContentAndScrollbarBounds()
    {
        var routed = Create();
        ScrollableViewportFrameState withoutScrollbar = routed.CalculateFrame(3, 3, new Rect(1, 2, 8, 3), null);
        ScrollableViewportFrameState withScrollbar = routed.CalculateFrame(10, 3, new Rect(1, 2, 8, 3), new Rect(9, 2, 1, 3));

        Assert.Equal([routed.ContentTarget], routed.BuildInteractionFragment(withoutScrollbar).HitRegions.Select(region => region.Target));
        Assert.Equal(
            [(routed.ContentTarget, withScrollbar.ContentBounds), (routed.ScrollbarTarget, withScrollbar.ScrollbarBounds!.Value)],
            routed.BuildInteractionFragment(withScrollbar).HitRegions.Select(region => (region.Target, region.Bounds)));
        Assert.Empty(routed.BuildInteractionFragment(withScrollbar).FocusEntries);
    }

    [Fact]
    public void RouteInput_UsesTargetOwnershipAndInvalidatesOnlyWhenPositionChanges()
    {
        var routed = Create();
        ScrollableViewportFrameState frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 3));
        var focus = new UiFocusController();

        RoutedScrollableViewportInputResult foreign = routed.RouteInput(
            Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 2, 1),
            frame,
            UiInputRouteContext.HitTarget(focus, new UiTargetId("other")));
        RoutedScrollableViewportInputResult unchanged = routed.RouteInput(
            Mouse(MouseButton.WheelUp, MouseEventKind.Wheel, 2, 1),
            frame,
            UiInputRouteContext.HitTarget(focus, routed.ContentTarget),
            wheelStep: 99);
        RoutedScrollableViewportInputResult changed = routed.RouteInput(
            Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 2, 1),
            frame,
            UiInputRouteContext.HitTarget(focus, routed.ContentTarget));

        Assert.False(foreign.UiResult.Handled);
        Assert.True(changed.UiResult.Invalidate);
        Assert.True(unchanged.UiResult.Handled);
        Assert.False(unchanged.UiResult.Invalidate);
    }

    [Fact]
    public void RouteInput_RoutesCapturedScrollbarDragAndReleasesCapture()
    {
        var routed = Create();
        ScrollableViewportFrameState frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5));
        var focus = new UiFocusController();

        RoutedScrollableViewportInputResult foreignCaptured = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Move, 20, 3),
            frame,
            UiInputRouteContext.CapturedTarget(focus, new UiTargetId("other")));

        RoutedScrollableViewportInputResult started = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1),
            frame,
            UiInputRouteContext.HitTarget(focus, routed.ScrollbarTarget));
        frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5));
        RoutedScrollableViewportInputResult moved = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Move, 20, 3),
            frame,
            UiInputRouteContext.CapturedTarget(focus, routed.ScrollbarTarget));
        frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5));
        RoutedScrollableViewportInputResult ended = routed.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Up, 20, 3),
            frame,
            UiInputRouteContext.CapturedTarget(focus, routed.ScrollbarTarget));

        Assert.False(foreignCaptured.UiResult.Handled);
        Assert.True(started.ViewportResult.DragStarted);
        Assert.Equal(UiMouseCaptureRequestKind.Capture, started.UiResult.MouseCaptureRequest.Kind);
        Assert.Equal(routed.ScrollbarTarget, started.UiResult.MouseCaptureRequest.Target);
        Assert.True(moved.ViewportResult.IsHandled);
        Assert.True(ended.ViewportResult.DragEnded);
        Assert.Equal(UiMouseCaptureRequestKind.Release, ended.UiResult.MouseCaptureRequest.Kind);
    }

    [Fact]
    public void GetScrollState_ExposesScrollableFrameWithoutPrimitiveAccess()
    {
        var routed = Create();
        ScrollableViewportFrameState frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5));

        ScrollState state = Assert.IsType<ScrollState>(routed.GetScrollState(frame));

        Assert.Equal(10, state.TotalItems);
        Assert.Equal(3, state.ViewportItems);
    }

    [Fact]
    public void RenderScrollbar_UsesCalculatedFrameBoundsAndSkipsAbsentScrollbar()
    {
        var routed = Create();
        var driver = new FakeConsoleDriver(12, 8);
        var screen = new ScreenRenderer(driver);
        ScrollableViewportFrameState frame = routed.CalculateFrame(10, 3, new Rect(0, 0, 9, 3), new Rect(10, 1, 1, 5));

        UiTestRender.Render(screen, canvas => routed.RenderScrollbar(canvas, frame, new CellStyle(ConsoleColor.White, ConsoleColor.Black)));

        Assert.Equal('▲', driver.GetCell(10, 1).Character);
        Assert.Equal('▼', driver.GetCell(10, 5).Character);

        var emptyDriver = new FakeConsoleDriver(12, 8);
        UiTestRender.Render(new ScreenRenderer(emptyDriver), canvas =>
            routed.RenderScrollbar(canvas, routed.CalculateFrame(3, 3, new Rect(0, 0, 9, 3), null), new CellStyle(ConsoleColor.White, ConsoleColor.Black)));
        Assert.All(Enumerable.Range(0, 8), y => Assert.Equal(' ', emptyDriver.GetCell(10, y).Character));
    }

    private static RoutedScrollableViewport Create() =>
        new(new UiTargetId("viewport.content"), new UiTargetId("viewport.scrollbar"));

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x, int y) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
