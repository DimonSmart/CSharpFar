using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ScrollableViewportTests
{
    [Fact]
    public void CalculateFrameState_NormalizesOnlyTheReturnedFrameUntilCommitted()
    {
        var viewport = new ScrollableViewport { FirstVisibleIndex = 18 };

        ScrollableViewportFrameState frame = viewport.CalculateFrameState(
            20, 4, new Rect(0, 0, 9, 4), new Rect(9, 0, 1, 4));

        Assert.Equal(16, frame.FirstVisibleIndex);
        Assert.Equal(18, viewport.FirstVisibleIndex);
        viewport.ApplyCommittedFrame(frame);
        Assert.Equal(16, viewport.FirstVisibleIndex);
    }

    [Fact]
    public void HandleKey_NavigatesByLinePageAndBounds()
    {
        var viewport = new ScrollableViewport();
        ScrollableViewportFrameState frame = Frame(viewport);

        Assert.True(viewport.HandleKey(Key(ConsoleKey.DownArrow), frame).PositionChanged);
        Assert.Equal(1, viewport.FirstVisibleIndex);
        frame = Commit(viewport);
        Assert.True(viewport.HandleKey(Key(ConsoleKey.PageDown), frame).PositionChanged);
        Assert.Equal(4, viewport.FirstVisibleIndex);
        frame = Commit(viewport);
        Assert.True(viewport.HandleKey(Key(ConsoleKey.End), frame).PositionChanged);
        Assert.Equal(7, viewport.FirstVisibleIndex);
        frame = Commit(viewport);
        Assert.True(viewport.HandleKey(Key(ConsoleKey.Home), frame).PositionChanged);
        Assert.Equal(0, viewport.FirstVisibleIndex);
    }

    [Fact]
    public void HandleMouse_WheelUsesCommittedBoundsOnly()
    {
        var viewport = new ScrollableViewport();
        ScrollableViewportFrameState frame = Frame(viewport);

        Assert.True(viewport.HandleMouse(Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 2, 1), frame).PositionChanged);
        Assert.Equal(3, viewport.FirstVisibleIndex);
        Assert.Equal(
            ScrollableViewportInputResultKind.NotHandled,
            viewport.HandleMouse(Mouse(MouseButton.WheelDown, MouseEventKind.Wheel, 20, 20), frame).Kind);
    }

    [Fact]
    public void HandleMouse_ScrollbarDragSignalsLifecycleAndClearsWhenScrollbarDisappears()
    {
        var viewport = new ScrollableViewport();
        ScrollableViewportFrameState frame = Frame(viewport);

        ScrollableViewportInputResult started = viewport.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1), frame);
        viewport.ApplyCommittedFrame(viewport.CalculateFrameState(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5)));
        frame = Frame(viewport);
        ScrollableViewportInputResult moved = viewport.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Move, 20, 3), frame);
        viewport.ApplyCommittedFrame(viewport.CalculateFrameState(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5)));
        frame = Frame(viewport);
        ScrollableViewportInputResult ended = viewport.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Up, 20, 3), frame);

        Assert.True(started.DragStarted);
        Assert.True(moved.IsHandled);
        Assert.True(ended.DragEnded);
        Assert.Null(frame.ScrollbarFrame?.DragState);

        viewport.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Down, 9, 1), frame);
        viewport.ApplyCommittedFrame(viewport.CalculateFrameState(3, 3, new Rect(0, 0, 9, 3), scrollbarBounds: null));
        Assert.Null(viewport.CalculateFrameState(3, 3, new Rect(0, 0, 9, 3), scrollbarBounds: null).ScrollbarFrame);
    }

    private static ScrollableViewportFrameState Frame(ScrollableViewport viewport) =>
        viewport.CalculateFrameState(10, 3, new Rect(0, 0, 9, 3), new Rect(9, 0, 1, 5));

    private static ScrollableViewportFrameState Commit(ScrollableViewport viewport)
    {
        ScrollableViewportFrameState frame = Frame(viewport);
        viewport.ApplyCommittedFrame(frame);
        return frame;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x, int y) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
