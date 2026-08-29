using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class RoutedPointerControlsTests
{
    [Fact]
    public void PointerCollection_PublishesSemanticItemTargetsAndResolvesThem()
    {
        var surface = new RoutedPointerCollection<int>(new UiTargetId("list"), item => new UiTargetId($"item:{item}"));
        RoutedPointerItem<int>[] items =
        [
            new(4, new Rect(1, 2, 8, 1)),
            new(9, new Rect(1, 3, 8, 1)),
        ];

        UiInteractionFragment fragment = surface.BuildInteractionFragment(new Rect(0, 0, 12, 8), items);

        Assert.Equal(["list", "item:4", "item:9"], fragment.HitRegions.Select(region => region.Target.Value));
        RoutedPointerInput<int> action = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 2, 3),
            UiInputRouteContext.HitTarget(new UiFocusController(), new UiTargetId("item:9")), items);
        Assert.Equal(RoutedPointerActionKind.ItemPrimaryPressed, action.Action.Kind);
        Assert.Equal(9, action.Action.Item);
    }

    [Fact]
    public void ScrollbarSurface_ThumbDragCapturesMovesAndReleases()
    {
        var target = new UiTargetId("scrollbar");
        var surface = new RoutedScrollbarSurface(target);
        VerticalScrollbarFrame frame = Assert.IsType<VerticalScrollbarFrame>(surface.CalculateFrame(
            new Rect(0, 0, 1, 10),
            new ScrollState { TotalItems = 100, ViewportItems = 10, FirstVisibleIndex = 0 }));
        surface.ApplyCommittedFrame(frame);
        var focus = new UiFocusController();

        RoutedScrollbarSurfaceInput down = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 0, 1), frame,
            UiInputRouteContext.HitTarget(focus, target));
        RoutedScrollbarSurfaceInput move = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Move, 0, 8), frame,
            UiInputRouteContext.CapturedTarget(focus, target));
        RoutedScrollbarSurfaceInput up = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Up, 0, 8), frame,
            UiInputRouteContext.CapturedTarget(focus, target));

        Assert.Null(down.FirstVisibleIndex);
        Assert.Equal(UiMouseCaptureRequestKind.Capture, down.UiResult.MouseCaptureRequest.Kind);
        Assert.NotNull(move.FirstVisibleIndex);
        Assert.Equal(UiMouseCaptureRequestKind.Release, up.UiResult.MouseCaptureRequest.Kind);
    }

    [Fact]
    public void PointerCaptureSurface_OnlyCapturesItsOwnLeftDrag()
    {
        var target = new UiTargetId("command-line");
        var surface = new RoutedPointerCaptureSurface(target);
        var focus = new UiFocusController();

        UiInputResult down = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Down, 1, 1),
            UiInputRouteContext.HitTarget(focus, target));
        UiInputResult up = surface.RouteInput(
            Mouse(MouseButton.Left, MouseEventKind.Up, 1, 1),
            UiInputRouteContext.CapturedTarget(focus, target));

        Assert.Equal(UiMouseCaptureRequestKind.Capture, down.MouseCaptureRequest.Kind);
        Assert.Equal(UiMouseCaptureRequestKind.Release, up.MouseCaptureRequest.Kind);
        Assert.Single(surface.BuildInteractionFragment(new Rect(0, 0, 10, 1)).HitRegions);
    }

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind, int x, int y) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
