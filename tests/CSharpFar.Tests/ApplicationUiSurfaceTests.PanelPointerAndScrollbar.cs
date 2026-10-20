using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class ApplicationUiSurfaceTests
{
    [Fact]
    public void HiddenCommandLineMode_PublishesOnlyCommandLineHitTarget()
    {
        var services = Services();
        services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;

        services.Composition.Render();

        var regions = services.ApplicationSurface.CommittedInteractionFrame.HitRegions;
        var region = Assert.Single(regions);
        Assert.Equal(ApplicationTargetIds.CommandLine, region.Target);
        Assert.Null(services.ApplicationSurface.CommittedFrame.LeftPanel);
        Assert.Null(services.ApplicationSurface.CommittedFrame.RightPanel);
        Assert.Null(services.ApplicationSurface.CommittedFrame.FunctionKeyBar);
        Assert.Null(services.ApplicationSurface.CommittedFrame.DirectoryShortcutBar);
    }

    [Fact]
    public void QuickViewPassiveSide_DoesNotPublishFilePanelTarget()
    {
        var services = Services();
        services.Session.App.QuickView = true;

        services.Composition.Render();

        Assert.NotNull(services.ApplicationSurface.CommittedFrame.LeftPanel);
        Assert.Null(services.ApplicationSurface.CommittedFrame.RightPanel);
        Assert.Contains(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
            region => region.Target == ApplicationTargetIds.LeftPanel);
        Assert.DoesNotContain(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
            region => region.Target == ApplicationTargetIds.RightPanel);
    }

    [Fact]
    public void NarrowViewport_DoesNotPublishEmptyPanelTargets()
    {
        var services = Services(new FakeConsoleDriver(1, 1));

        services.Composition.Render();

        Assert.DoesNotContain(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
            region =>
                region.Target == ApplicationTargetIds.LeftPanel ||
                region.Target == ApplicationTargetIds.RightPanel ||
                region.Target == ApplicationTargetIds.LeftPanelScrollbar ||
                region.Target == ApplicationTargetIds.RightPanelScrollbar);
    }

    [Fact]
    public void PanelScrollbarHit_WinsOverPanelAndThumbDownRequestsCapture()
    {
        var services = Services();
        AddScrollableItems(services.Session.Panels.Left, 40);

        services.Composition.Render();

        var scrollbar = services.ApplicationSurface.CommittedFrame.LeftPanel?.ScrollBar;
        Assert.NotNull(scrollbar);
        Assert.True(ScrollBarInteraction.IsInteractive(scrollbar.Bounds, scrollbar.ToScrollState()));

        var regions = services.ApplicationSurface.CommittedInteractionFrame.HitRegions;
        int panelIndex = regions.ToList().FindIndex(region => region.Target == ApplicationTargetIds.LeftPanel);
        int scrollbarIndex = regions.ToList().FindIndex(region => region.Target == ApplicationTargetIds.LeftPanelScrollbar);
        Assert.True(panelIndex >= 0);
        Assert.True(scrollbarIndex > panelIndex);

        var thumb = ScrollBarInteraction.CalculateThumb(scrollbar.Bounds, scrollbar.ToScrollState());
        Assert.True(services.ApplicationSurface.CommittedInteractionFrame.TryHitTest(
            scrollbar.Bounds.X,
            thumb.ThumbY,
            out var hit));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, hit.Target);

        UiInputResult result = services.Composition.DispatchInput(new MouseConsoleInputEvent(
            scrollbar.Bounds.X,
            thumb.ThumbY,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, routed.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, routed.RouteKind);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            79,
            22,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var captured));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, captured.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, captured.RouteKind);
    }

    [Fact]
    public void PanelScrollbarDrag_RoutesThroughSurfaceAndHandlerUntilMatchingLeftUp()
    {
        var services = Services();
        AddScrollableItems(services.Session.Panels.Left, 80);
        services.Composition.Render();

        var scrollbar = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        var thumb = ScrollBarInteraction.CalculateThumb(scrollbar.Bounds, scrollbar.ToScrollState());

        UiInputResult downResult = services.Composition.DispatchInput(new MouseConsoleInputEvent(
            scrollbar.Bounds.X,
            thumb.ThumbY,
            MouseButton.Left,
            MouseEventKind.Down,
            MouseKeyModifiers.None));
        Assert.True(downResult.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var down));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, down.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, down.RouteKind);

        services.Inner.ApplicationInputDispatcher.Handle(down);
        Assert.Null(down.PointerInteraction);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            79,
            scrollbar.Bounds.Bottom - 1,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, move.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, move.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(move);
        Assert.True(services.Session.Panels.Left.ScrollOffset > 0);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            79,
            scrollbar.Bounds.Bottom - 1,
            MouseButton.Right,
            MouseEventKind.Up,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var nonMatchingUp));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, nonMatchingUp.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, nonMatchingUp.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(nonMatchingUp);
        Assert.Null(nonMatchingUp.PointerInteraction);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            0,
            0,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var stillCaptured));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, stillCaptured.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, stillCaptured.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(stillCaptured);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            0,
            0,
            MouseButton.Left,
            MouseEventKind.Up,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var leftUp));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, leftUp.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, leftUp.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(leftUp);
        Assert.Null(leftUp.PointerInteraction);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            0,
            0,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var afterRelease));
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, afterRelease.RouteKind);
    }

    [Fact]
    public void PanelScrollbarDrag_CommittedResizeRebasesCaptureAndUsesNewGeometry()
    {
        var services = Services();
        AddScrollableItems(services.Session.Panels.Left, 80);
        services.Composition.Render();
        StartLeftScrollbarDrag(services);

        ApplicationScrollBarFrame before = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        services.Driver.SetSize(100, 35);
        services.Composition.Render();

        ApplicationScrollBarFrame after = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        ScrollBarDragState drag = Assert.IsType<ScrollBarDragState>(after.VerticalScrollbarFrame?.DragState);
        Assert.NotEqual(before.Bounds, after.Bounds);
        Assert.Equal(after.Bounds, drag.Bounds);
        Assert.Equal(after.TotalItems, drag.TotalItems);
        Assert.Equal(after.ViewportItems, drag.ViewportItems);

        int expectedOffset = ScrollBarInteraction.FirstVisibleIndexForThumbY(
            after.Bounds,
            after.ToScrollState(),
            after.Bounds.Bottom - 1,
            drag.PointerOffsetInThumb);
        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            0, after.Bounds.Bottom - 1, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, move.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, move.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(move);
        Assert.Equal(expectedOffset, services.Session.Panels.Left.ScrollOffset);
    }

    [Theory]
    [InlineData(ScrollbarTargetRemoval.HiddenCommandLine)]
    [InlineData(ScrollbarTargetRemoval.NotScrollable)]
    [InlineData(ScrollbarTargetRemoval.QuickViewPassiveSide)]
    public void PanelScrollbarDrag_CommittedTargetRemovalClearsCaptureAndDrag(ScrollbarTargetRemoval removal)
    {
        var services = Services();
        PanelSide side = removal == ScrollbarTargetRemoval.QuickViewPassiveSide ? PanelSide.Right : PanelSide.Left;
        AddScrollableItems(side == PanelSide.Left ? services.Session.Panels.Left : services.Session.Panels.Right, 80);
        services.Composition.Render();
        StartScrollbarDrag(services, side);

        switch (removal)
        {
            case ScrollbarTargetRemoval.HiddenCommandLine:
                services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;
                break;
            case ScrollbarTargetRemoval.NotScrollable:
                AddScrollableItems(services.Session.Panels.Left, 1);
                break;
            case ScrollbarTargetRemoval.QuickViewPassiveSide:
                services.Session.Panels.ActiveSide = PanelSide.Left;
                services.Session.App.QuickView = true;
                break;
        }

        services.Composition.Render();

        UiTargetId target = ApplicationTargetIds.PanelScrollbar(side);
        Assert.DoesNotContain(services.ApplicationSurface.CommittedInteractionFrame.HitRegions, region => region.Target == target);
        services.Composition.DispatchInput(new MouseConsoleInputEvent(0, 0, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, move.RouteKind);
        Assert.NotEqual(target, move.Target);
        services.Inner.ApplicationInputDispatcher.Handle(move);
    }

    [Fact]
    public void CtrlO_DuringPanelScrollbarDrag_ClearsCaptureAndDrag()
    {
        var services = Services();
        AddScrollableItems(services.Session.Panels.Left, 80);
        services.Composition.Render();
        StartLeftScrollbarDrag(services);

        ApplicationScrollBarFrame scrollbar = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            scrollbar.Bounds.X,
            scrollbar.Bounds.Bottom - 1,
            MouseButton.Left,
            MouseEventKind.Move,
            MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var captured));
        Assert.Equal(UiInputRouteKind.CapturedTarget, captured.RouteKind);
        Assert.Equal(ApplicationTargetIds.LeftPanelScrollbar, captured.Target);
        services.Inner.ApplicationInputDispatcher.Handle(captured);

        var key = new KeyConsoleInputEvent(new ConsoleKeyInfo('\u000f', ConsoleKey.O, shift: false, alt: false, control: true));
        services.Composition.DispatchInput(key);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var keyPacket));
        ApplicationRuntimeRenderRequest request = services.Inner.ApplicationInputDispatcher.Handle(keyPacket);
        if (request.ShouldRender)
            services.Composition.Render();

        Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, services.Session.App.WorkspaceMode);
        Assert.DoesNotContain(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
            region => region.Target == ApplicationTargetIds.LeftPanelScrollbar);

        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            0, 0, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.NotEqual(UiInputRouteKind.CapturedTarget, move.RouteKind);
        Assert.NotEqual(ApplicationTargetIds.LeftPanelScrollbar, move.Target);
    }

    [Fact]
    public void PanelScrollbarDrag_RejectedRenderKeepsCommittedGeometryUntilRetry()
    {
        var services = Services();
        AddScrollableItems(services.Session.Panels.Left, 80);
        services.Composition.Render();
        StartLeftScrollbarDrag(services);

        ApplicationUiFrame committedFrame = services.ApplicationSurface.CommittedFrame;
        Rect committedBounds = committedFrame.LeftPanel!.ScrollBar!.Bounds;
        bool observedRejectedAttempt = false;
        services.Driver.BeforeTrySetCursorPositionInViewport = driver =>
        {
            observedRejectedAttempt = true;
            Assert.Same(committedFrame, services.ApplicationSurface.CommittedFrame);
            Assert.Equal(committedBounds, services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!.Bounds);
            driver.SetSize(100, 35);
            driver.BeforeTrySetCursorPositionInViewport = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        ApplicationScrollBarFrame retried = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        ScrollBarDragState drag = Assert.IsType<ScrollBarDragState>(retried.VerticalScrollbarFrame?.DragState);
        Assert.Equal(retried.Bounds, drag.Bounds);
        Assert.NotEqual(committedBounds, drag.Bounds);
    }

}
