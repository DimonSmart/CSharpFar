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
        services.Session.CommandLine.Completion.List.ResetItems(["", "alpha"]);

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

}
