using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class VerticalScrollbarControllerTests
{
    [Theory]
    [InlineData(0, 4)]
    [InlineData(9, 6)]
    public void HandleMouse_ClickButtonsChangesPosition(int y, int expected)
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller, firstVisibleIndex: 5);

        VerticalScrollbarInputResult result = controller.HandleMouse(LeftDown(0, y), frame);

        Assert.True(result.IsHandled);
        Assert.True(result.PositionChanged);
        Assert.Equal(expected, result.FirstVisibleIndex);
        Assert.False(result.DragStarted);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(8, 20)]
    public void HandleMouse_ClickTrackMovesByPage(int y, int expected)
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller, firstVisibleIndex: 10);

        VerticalScrollbarInputResult result = controller.HandleMouse(LeftDown(0, y), frame);

        Assert.True(result.IsHandled);
        Assert.True(result.PositionChanged);
        Assert.Equal(expected, result.FirstVisibleIndex);
    }

    [Fact]
    public void HandleMouse_ThumbDragSignalsCaptureMoveAndRelease()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller);

        VerticalScrollbarInputResult down = controller.HandleMouse(LeftDown(0, 1), frame);
        VerticalScrollbarInputResult move = controller.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Move, 20, 8), frame);
        VerticalScrollbarInputResult up = controller.HandleMouse(Mouse(MouseButton.Left, MouseEventKind.Up, 20, 8), frame);

        Assert.True(down.DragStarted);
        Assert.True(move.IsHandled);
        Assert.True(move.PositionChanged);
        Assert.True(move.FirstVisibleIndex > 50);
        Assert.True(up.DragEnded);
        Assert.Null(controller.DragState);
    }

    [Fact]
    public void HandleMouse_MouseUpWithoutDragDoesNotRelease()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller);

        VerticalScrollbarInputResult result = controller.HandleMouse(
            Mouse(MouseButton.Left, MouseEventKind.Up, 0, 1),
            frame);

        Assert.False(result.IsHandled);
        Assert.False(result.DragEnded);
    }

    [Fact]
    public void ApplyCommittedFrame_AcceptedResizeRebasesDrag()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame oldFrame = Commit(controller);
        controller.HandleMouse(LeftDown(0, 1), oldFrame);

        VerticalScrollbarFrame newFrame = Assert.IsType<VerticalScrollbarFrame>(
            controller.CalculateFrame(new Rect(3, 2, 1, 14), State(firstVisibleIndex: 0)));
        controller.ApplyCommittedFrame(newFrame);

        ScrollBarDragState drag = Assert.IsType<ScrollBarDragState>(controller.DragState);
        Assert.Equal(new Rect(3, 2, 1, 14), drag.Bounds);
        Assert.Equal(100, drag.TotalItems);
        Assert.Equal(10, drag.ViewportItems);
    }

    [Fact]
    public void CalculateFrame_RejectedCandidateDoesNotChangeCommittedDragGeometry()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame oldFrame = Commit(controller);
        controller.HandleMouse(LeftDown(0, 1), oldFrame);

        _ = controller.CalculateFrame(new Rect(3, 2, 1, 14), State(firstVisibleIndex: 0));

        ScrollBarDragState drag = Assert.IsType<ScrollBarDragState>(controller.DragState);
        Assert.Equal(oldFrame.Bounds, drag.Bounds);
    }

    [Fact]
    public void ApplyCommittedFrame_MissingOrNonInteractiveScrollbarClearsDrag()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller);
        controller.HandleMouse(LeftDown(0, 1), frame);

        controller.ApplyCommittedFrame(null);

        Assert.Null(controller.DragState);
        Assert.Null(controller.CalculateFrame(new Rect(0, 0, 1, 10), State(totalItems: 10, viewportItems: 10)));
    }

    [Fact]
    public void HandleMouse_PositionIsClamped()
    {
        var controller = new VerticalScrollbarController();
        VerticalScrollbarFrame frame = Commit(controller, firstVisibleIndex: 90);

        VerticalScrollbarInputResult result = controller.HandleMouse(LeftDown(0, 9), frame);

        Assert.True(result.IsHandled);
        Assert.Equal(90, result.FirstVisibleIndex);
        Assert.False(result.PositionChanged);
    }

    private static VerticalScrollbarFrame Commit(
        VerticalScrollbarController controller,
        int firstVisibleIndex = 0)
    {
        VerticalScrollbarFrame frame = Assert.IsType<VerticalScrollbarFrame>(
            controller.CalculateFrame(new Rect(0, 0, 1, 10), State(firstVisibleIndex: firstVisibleIndex)));
        controller.ApplyCommittedFrame(frame);
        return frame;
    }

    private static ScrollState State(
        int totalItems = 100,
        int viewportItems = 10,
        int firstVisibleIndex = 0) => new()
        {
            TotalItems = totalItems,
            ViewportItems = viewportItems,
            FirstVisibleIndex = firstVisibleIndex,
        };

    private static MouseConsoleInputEvent LeftDown(int x, int y) =>
        Mouse(MouseButton.Left, MouseEventKind.Down, x, y);

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x, int y) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
