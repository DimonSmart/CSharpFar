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

public sealed class ApplicationUiSurfaceTests
{
    [Fact]
    public void SurfaceContract_UsesApplicationSurfaceAsInteractiveRoot()
    {
        var services = Services();
        var surface = services.ApplicationSurface;

        Assert.IsAssignableFrom<IUiSurface>(surface);
        Assert.IsAssignableFrom<IUiLayer>(surface);
        Assert.Equal(UiLayerInputPolicy.Bubble, surface.InputPolicy);
        Assert.NotSame(new UiFocusScope(), surface.FocusScope);

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(surface.TryTakeInput(out _));
    }

    [Fact]
    public void Render_CommitsApplicationFrameForVisibleAndHiddenCommandLine()
    {
        var services = Services();

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var visible));
        Assert.Equal(new ConsoleViewport(0, 0, 80, 25), visible.Frame.Viewport);
        Assert.Equal(ApplicationWorkspaceMode.Panels, visible.Frame.Mode);
        Assert.Equal(new Rect(0, 23, 80, 1), visible.Frame.CommandLine.Bounds);
        Assert.NotNull(visible.Frame.CommandLine.Cursor);

        services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var hidden));

        Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, hidden.Frame.Mode);
        Assert.Equal(new Rect(0, 23, 80, 1), hidden.Frame.CommandLine.Bounds);
    }

    [Fact]
    public void RejectedRenderAttempt_DoesNotBecomeCommittedFrame()
    {
        var driver = new FakeConsoleDriver(80, 25)
        {
            ResizeAfterWriteCount = 1,
            ResizeAfterWrite = d => d.SetSize(100, 35),
        };
        var services = Services(driver);

        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), routed.Frame.Viewport);
    }

    [Fact]
    public void PendingPacketKeepsDispatchFrameAfterAdditionalRender()
    {
        var services = Services();
        services.Composition.Render();
        services.Composition.DispatchInput(Key(ConsoleKey.A));
        services.Driver.SetSize(100, 35);
        services.Composition.Render();

        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(new ConsoleViewport(0, 0, 80, 25), routed.Frame.Viewport);
        Assert.Equal(ApplicationTargetIds.CommandLine, routed.Target);
        Assert.Equal(UiInputRouteKind.FocusedTarget, routed.RouteKind);
    }

    [Fact]
    public void CommandLineHit_CreatesTargetRouteAndCaptureRequest()
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(
            new MouseConsoleInputEvent(10, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ApplicationTargetIds.CommandLine, routed.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, routed.RouteKind);
        Assert.Equal(new ConsoleViewport(0, 0, 80, 25), routed.Frame.Viewport);

        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(79, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var captured));
        Assert.Equal(ApplicationTargetIds.CommandLine, captured.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, captured.RouteKind);
    }

    [Fact]
    public void CommandLineCaptureLifecycle_RoutesCapturedEventsThenReleasesBeforeNextMove()
    {
        var services = Services();
        services.Composition.Render();

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(10, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            UiInputRouteKind.HitTarget);
        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(79, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);
        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(79, 10, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);

        UiInputResult nextResult = services.Composition.DispatchInput(
            new MouseConsoleInputEvent(79, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(nextResult.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var next));
        Assert.Equal(ApplicationTargetIds.RightPanel, next.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, next.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(next);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void CommandLineCapture_NonMatchingUpPreservesCaptureUntilMatchingLeftUp()
    {
        var services = Services();
        services.Composition.Render();

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(10, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            UiInputRouteKind.HitTarget);
        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(10, 23, MouseButton.Right, MouseEventKind.Up, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);
        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(0, 23, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);
        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(0, 23, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);
    }

    [Fact]
    public void CommandLineCapture_AfterCommittedResizeUsesNewFrame()
    {
        var services = Services();
        services.Session.CommandLine.State.SetText("abcdef");
        services.Composition.Render();

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(10, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            UiInputRouteKind.HitTarget);

        services.Driver.SetSize(100, 35);
        services.Composition.Render();
        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(99, 33, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.Equal(UiInputRouteKind.CapturedTarget, move.RouteKind);
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), move.Frame.Viewport);
        Assert.Equal(new Rect(0, 33, 100, 1), move.Frame.CommandLine.Bounds);
        services.Inner.ApplicationInputDispatcher.Handle(move);
        Assert.Equal(6, services.Session.CommandLine.State.CursorPosition);

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(99, 33, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);
    }

    [Fact]
    public void CommandLineCapture_RejectedRenderAttemptKeepsCommittedFrameUntilRetry()
    {
        var services = Services();
        services.Session.CommandLine.State.SetText(new string('x', 120));
        services.Composition.Render();

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(79, 23, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            UiInputRouteKind.HitTarget);

        ApplicationUiFrame committedFrame = services.ApplicationSurface.CommittedFrame;
        ApplicationCommandLineFrame committedCommandLine = committedFrame.CommandLine;
        var selectionAndCursor = (
            services.Session.CommandLine.State.CursorPosition,
            services.Session.CommandLine.State.SelectionStart,
            services.Session.CommandLine.State.SelectionLength);
        bool observedRejectedAttempt = false;

        services.Driver.BeforeTrySetCursorPositionInViewport = driver =>
        {
            observedRejectedAttempt = true;
            Assert.Same(committedFrame, services.ApplicationSurface.CommittedFrame);
            Assert.Same(committedCommandLine, services.ApplicationSurface.CommittedFrame.CommandLine);
            Assert.Equal(selectionAndCursor, (
                services.Session.CommandLine.State.CursorPosition,
                services.Session.CommandLine.State.SelectionStart,
                services.Session.CommandLine.State.SelectionLength));
            driver.SetSize(100, 35);
            driver.BeforeTrySetCursorPositionInViewport = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        ApplicationUiFrame retriedFrame = services.ApplicationSurface.CommittedFrame;
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), retriedFrame.Viewport);
        Assert.Equal(new Rect(0, 33, 100, 1), retriedFrame.CommandLine.Bounds);

        int oldPosition = committedCommandLine.TextPositionFromX(0);
        int expectedPosition = retriedFrame.CommandLine.TextPositionFromX(0);
        Assert.NotEqual(oldPosition, expectedPosition);

        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(0, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var move));
        Assert.Equal(ApplicationTargetIds.CommandLine, move.Target);
        Assert.Equal(UiInputRouteKind.CapturedTarget, move.RouteKind);
        Assert.Same(retriedFrame, move.Frame);
        services.Inner.ApplicationInputDispatcher.Handle(move);
        Assert.Equal(expectedPosition, services.Session.CommandLine.State.CursorPosition);

        DispatchTakeAndHandle(
            services,
            new MouseConsoleInputEvent(0, 33, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            UiInputRouteKind.CapturedTarget);

        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(0, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(services.ApplicationSurface.TryTakeInput(out var next));
        Assert.Equal(ApplicationTargetIds.LeftPanel, next.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, next.RouteKind);
    }

    [Fact]
    public void CommandLineCursor_PhysicalCursorMatchesCommittedBaseFrame()
    {
        var services = Services();

        services.Composition.Render();

        Assert.True(services.Driver.CursorVisible);
        UiCursorPlacement cursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;
        Assert.Equal(cursor.X, services.Driver.CursorX);
        Assert.Equal(cursor.Y, services.Driver.CursorY);
    }

    [Fact]
    public void CommandLineCursor_HiddenCommandLineUsesCommittedCommandLineRow()
    {
        var services = Services();
        services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;

        services.Composition.Render();

        UiCursorPlacement cursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;
        Assert.True(services.Driver.CursorVisible);
        Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, services.ApplicationSurface.CommittedFrame.Mode);
        Assert.Equal(cursor.X, services.Driver.CursorX);
        Assert.Equal(cursor.Y, services.Driver.CursorY);
    }

    [Fact]
    public void CommandLineCursor_CommandCompletionDoesNotHideLowerCursor()
    {
        var services = Services();
        services.Session.CommandLine.Completion.Visible = true;
        services.Session.CommandLine.Completion.Matches.AddRange(["", "alpha"]);

        services.Composition.Render();

        UiCursorPlacement cursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;
        Assert.True(services.Driver.CursorVisible);
        Assert.Equal(cursor.X, services.Driver.CursorX);
        Assert.Equal(cursor.Y, services.Driver.CursorY);
    }

    [Fact]
    public void CommandLineCursor_QuickSearchAndTopMenuOverrideThenRestoreLowerCursor()
    {
        var services = Services();

        services.Composition.Render();
        UiCursorPlacement commandCursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;

        services.Composition.DispatchInput(Key(ConsoleKey.G, keyChar: 'g', alt: true));
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        services.Composition.Render();
        Assert.True(services.Driver.CursorVisible);
        Assert.NotEqual((commandCursor.X, commandCursor.Y), (services.Driver.CursorX, services.Driver.CursorY));

        services.Composition.DispatchInput(Key(ConsoleKey.F9));
        services.Composition.Render();
        Assert.False(services.Driver.CursorVisible);

        services.Composition.DispatchInput(Key(ConsoleKey.Escape));
        services.Composition.Render();
        commandCursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;
        Assert.True(services.Driver.CursorVisible);
        Assert.Equal((commandCursor.X, commandCursor.Y), (services.Driver.CursorX, services.Driver.CursorY));
    }

    [Fact]
    public void CommandLineCursor_RejectedAttemptKeepsPreviousPhysicalCursorAndMetadataUntilCommit()
    {
        var services = Services();
        services.Composition.Render();
        var committedCursor = services.ApplicationSurface.CommittedFrame.CommandLine.Cursor;
        var physicalCursor = (services.Driver.CursorVisible, services.Driver.CursorX, services.Driver.CursorY);
        bool observedRejectedAttempt = false;

        services.Session.CommandLine.State.SetText(new string('x', 30));
        services.Driver.BeforeTrySetCursorPositionInViewport = current =>
        {
            observedRejectedAttempt = true;
            Assert.Equal(physicalCursor, (current.CursorVisible, current.CursorX, current.CursorY));
            Assert.Equal(committedCursor, services.ApplicationSurface.CommittedFrame.CommandLine.Cursor);
            current.SetSize(100, 35);
            current.BeforeTrySetCursorPositionInViewport = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        Assert.Equal(new ConsoleViewport(0, 0, 100, 35), services.ApplicationSurface.CommittedFrame.Viewport);
        Assert.Equal(services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value.X, services.Driver.CursorX);
        Assert.Equal(services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value.Y, services.Driver.CursorY);
    }

    [Fact]
    public void CommandLineCursorMetadata_ComesFromCommittedFrame()
    {
        var services = Services();

        services.Composition.Render();

        UiFocusEntry focus = Assert.Single(
            services.ApplicationSurface.CommittedInteractionFrame.Focus.Entries,
            entry => entry.Target == ApplicationTargetIds.CommandLine);
        Assert.Equal(ApplicationTargetIds.CommandLine, focus.Target);
        Assert.Equal(services.ApplicationSurface.CommittedFrame.CommandLine.Cursor, focus.Cursor);
        Assert.Equal(ApplicationTargetIds.CommandLine, services.ApplicationSurface.CommittedInteractionFrame.Focus.DefaultTarget);
        Assert.Contains(
            services.ApplicationSurface.CommittedInteractionFrame.Focus.Entries,
            entry => entry.Target == ApplicationTargetIds.LeftPanel);
        Assert.Contains(
            services.ApplicationSurface.CommittedInteractionFrame.Focus.Entries,
            entry => entry.Target == ApplicationTargetIds.RightPanel);
        Assert.Contains(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
            region => region.Target == ApplicationTargetIds.CommandLine);
    }

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
        Assert.NotNull(services.Session.Ui.PanelScrollbarDrag);

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
        Assert.NotNull(services.Session.Ui.PanelScrollbarDrag);

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
        Assert.Null(services.Session.Ui.PanelScrollbarDrag);

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
        PanelScrollbarDrag drag = Assert.IsType<PanelScrollbarDrag>(services.Session.Ui.PanelScrollbarDrag);
        Assert.NotEqual(before.Bounds, after.Bounds);
        Assert.Equal(after.Bounds, drag.DragState.Bounds);
        Assert.Equal(after.TotalItems, drag.DragState.TotalItems);
        Assert.Equal(after.ViewportItems, drag.DragState.ViewportItems);

        int expectedOffset = ScrollBarInteraction.FirstVisibleIndexForThumbY(
            after.Bounds,
            after.ToScrollState(),
            after.Bounds.Bottom - 1,
            drag.DragState.PointerOffsetInThumb);
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
        Assert.Null(services.Session.Ui.PanelScrollbarDrag);
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
        Assert.Null(services.Session.Ui.PanelScrollbarDrag);

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
        PanelScrollbarDrag committedDrag = Assert.IsType<PanelScrollbarDrag>(services.Session.Ui.PanelScrollbarDrag);
        bool observedRejectedAttempt = false;
        services.Driver.BeforeTrySetCursorPositionInViewport = driver =>
        {
            observedRejectedAttempt = true;
            Assert.Same(committedFrame, services.ApplicationSurface.CommittedFrame);
            Assert.Equal(committedDrag, services.Session.Ui.PanelScrollbarDrag);
            driver.SetSize(100, 35);
            driver.BeforeTrySetCursorPositionInViewport = null;
        };

        services.Composition.Render();

        Assert.True(observedRejectedAttempt);
        ApplicationScrollBarFrame retried = services.ApplicationSurface.CommittedFrame.LeftPanel!.ScrollBar!;
        PanelScrollbarDrag drag = Assert.IsType<PanelScrollbarDrag>(services.Session.Ui.PanelScrollbarDrag);
        Assert.Equal(retried.Bounds, drag.DragState.Bounds);
        Assert.NotEqual(committedDrag.DragState.Bounds, drag.DragState.Bounds);
    }

    [Fact]
    public void FunctionKeyAndShortcutTargets_UseOnlyRenderedSlots()
    {
        var services = Services();
        services.Inner.Settings.DirectoryShortcuts.Items.Add(new AppSettings.DirectoryShortcutItem
        {
            Number = 1,
            Name = "Root",
            Path = @"C:\Root",
        });

        services.Composition.Render();

        Assert.NotNull(services.ApplicationSurface.CommittedFrame.FunctionKeyBar);
        Assert.NotNull(services.ApplicationSurface.CommittedFrame.DirectoryShortcutBar);
        Assert.All(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions
                .Where(region => region.Target == ApplicationTargetIds.FunctionKeyBar),
            region => Assert.Contains(
                services.ApplicationSurface.CommittedFrame.FunctionKeyBar!.Actions,
                action => action.Bounds.Equals(region.Bounds)));
        Assert.All(
            services.ApplicationSurface.CommittedInteractionFrame.HitRegions
                .Where(region => region.Target == ApplicationTargetIds.DirectoryShortcutBar),
            region => Assert.Contains(
                services.ApplicationSurface.CommittedFrame.DirectoryShortcutBar!.Shortcuts,
                shortcut => shortcut.Bounds.Equals(region.Bounds)));
    }

    [Theory]
    [MemberData(nameof(RoutedInputs))]
    public void SupportedSemanticInput_CreatesOneApplicationPacket(ConsoleInputEvent input, Type expectedType)
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.IsType(expectedType, routed.Input);
        if (input is KeyConsoleInputEvent or ModifierKeyConsoleInputEvent)
        {
            Assert.Equal(ApplicationTargetIds.CommandLine, routed.Target);
            Assert.Equal(UiInputRouteKind.FocusedTarget, routed.RouteKind);
        }
        else
        {
            Assert.Equal(ApplicationTargetIds.LeftPanel, routed.Target);
            Assert.Equal(UiInputRouteKind.HitTarget, routed.RouteKind);
        }
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void UnsupportedSemanticInput_DoesNotCreatePacket()
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(new ConsoleResizeInputEvent());

        Assert.False(result.Handled);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void SecondDispatchBeforeConsumeThrows()
    {
        var services = Services();
        services.Composition.Render();

        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.Throws<InvalidOperationException>(() => services.Composition.DispatchInput(Key(ConsoleKey.B)));
    }

    [Fact]
    public void BubbleOverlayIsolation_ControlsApplicationPacketOwnership()
    {
        var services = Services();
        services.Composition.Render();
        var handled = new TestLayer(UiLayerInputPolicy.Bubble) { Result = UiInputResult.HandledResult };
        using (services.Composition.PushOverlay(handled))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var unhandled = new TestLayer(UiLayerInputPolicy.Bubble);
        using (services.Composition.PushOverlay(unhandled))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void ModalAndTemporarySurface_IsolateApplicationInput()
    {
        var services = Services();
        services.Composition.Render();
        var modal = new TestLayer(UiLayerInputPolicy.Modal);
        using (services.Composition.PushOverlay(modal))
            services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        var temporary = new TestSurface(services.Composition.Screen, UiInputResult.HandledResult);
        using (services.Composition.OpenSurface(temporary))
            services.Composition.DispatchInput(Key(ConsoleKey.B));

        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.C));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ConsoleKey.C, Assert.IsType<KeyConsoleInputEvent>(routed.Input).Key.Key);
        Assert.Equal(ApplicationTargetIds.CommandLine, routed.Target);
        Assert.Equal(UiInputRouteKind.FocusedTarget, routed.RouteKind);
    }

    [Fact]
    public void RenderOnlyOverlay_DoesNotBlockApplicationInput()
    {
        var services = Services();
        services.Composition.Render();

        using var overlay = services.Composition.PushOverlay(_ => { });
        services.Composition.DispatchInput(Key(ConsoleKey.A));

        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    public static TheoryData<ConsoleInputEvent, Type> RoutedInputs() => new()
    {
        { Key(ConsoleKey.A), typeof(KeyConsoleInputEvent) },
        { new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control), typeof(ModifierKeyConsoleInputEvent) },
        { Mouse(), typeof(MouseConsoleInputEvent) },
    };

    private static TestServices Services(FakeConsoleDriver? driver = null)
    {
        driver ??= new FakeConsoleDriver(80, 25);
        var fs = new FakeFileSystemService();
        const string root = @"C:\Root";
        fs.AddDirectory(root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            enableBuiltInNetworkModules: false);
        _ = new Application(services);
        return new TestServices(driver, services);
    }

    private static KeyConsoleInputEvent Key(
        ConsoleKey key,
        char keyChar = '\0',
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        new(new ConsoleKeyInfo(keyChar, key, shift, alt, control));

    private static MouseConsoleInputEvent Mouse() =>
        new(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static void AddScrollableItems(FilePanelState state, int count)
    {
        state.Items.Clear();
        for (int i = 0; i < count; i++)
        {
            state.Items.Add(new FilePanelItem
            {
                Name = $"item-{i}.txt",
                FullPath = $@"C:\Root\item-{i}.txt",
                IsDirectory = false,
            });
        }
    }

    private static void StartLeftScrollbarDrag(TestServices services) =>
        StartScrollbarDrag(services, PanelSide.Left);

    private static void StartScrollbarDrag(TestServices services, PanelSide side)
    {
        ApplicationScrollBarFrame scrollbar = (side == PanelSide.Left
            ? services.ApplicationSurface.CommittedFrame.LeftPanel
            : services.ApplicationSurface.CommittedFrame.RightPanel)!.ScrollBar!;
        var thumb = ScrollBarInteraction.CalculateThumb(scrollbar.Bounds, scrollbar.ToScrollState());
        services.Composition.DispatchInput(new MouseConsoleInputEvent(
            scrollbar.Bounds.X, thumb.ThumbY, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var down));
        Assert.Equal(ApplicationTargetIds.PanelScrollbar(side), down.Target);
        services.Inner.ApplicationInputDispatcher.Handle(down);
        Assert.NotNull(services.Session.Ui.PanelScrollbarDrag);
    }

    public enum ScrollbarTargetRemoval
    {
        HiddenCommandLine,
        NotScrollable,
        QuickViewPassiveSide,
    }

    private static void DispatchTakeAndHandle(
        TestServices services,
        MouseConsoleInputEvent input,
        UiInputRouteKind expectedRouteKind)
    {
        UiInputResult result = services.Composition.DispatchInput(input);
        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.Equal(ApplicationTargetIds.CommandLine, routed.Target);
        Assert.Equal(expectedRouteKind, routed.RouteKind);
        services.Inner.ApplicationInputDispatcher.Handle(routed);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
    }

    private sealed record TestServices(FakeConsoleDriver Driver, ApplicationServices Inner)
    {
        public ApplicationUiSurface ApplicationSurface => Inner.ApplicationSurface;
        public UiCompositionHost Composition => Inner.Composition;
        public CSharpFar.App.State.ApplicationSession Session => Inner.Session;
    }

    private sealed class TestLayer(UiLayerInputPolicy policy) : IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => policy;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public UiInputResult Result { get; set; } = UiInputResult.NotHandled;
        public void Render(UiRenderContext context) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => Result;
    }

    private sealed class TestSurface(ScreenRenderer screen, UiInputResult result) : IUiSurface, IUiLayer
    {
        public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;
        public UiFocusScope FocusScope { get; } = new();
        public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) { }
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => result;
    }
}
