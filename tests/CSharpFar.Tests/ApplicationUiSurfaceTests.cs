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
    public void SurfaceContract_UsesApplicationSurfaceAsInteractiveRoot()
    {
        var services = Services();
        var surface = services.ApplicationSurface;

        Assert.IsAssignableFrom<IUiSurface>(surface);
        Assert.IsAssignableFrom<IUiLayer>(surface);
        Assert.Equal(UiLayerInputPolicy.Bubble, surface.InputPolicy);
        Assert.NotNull(surface.FocusState);
        Assert.False(surface.FocusState.HasFocus);

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
    public void CommittedPanelFrame_PublishesOverflowingItemForPassiveHoverOnly()
    {
        var services = Services(new FakeConsoleDriver(40, 12));
        FilePanelState left = services.Session.Panels.Left;
        left.Items.Clear();
        left.Items.Add(new FilePanelItem
        {
            Name = "a-very-long-file-name-that-overflows.txt",
            FullPath = @"C:\Root\a-very-long-file-name-that-overflows.txt",
            IsDirectory = false,
        });
        services.Composition.Render();
        ApplicationPanelItemHit hit = Assert.Single(services.ApplicationSurface.CommittedFrame.LeftPanel!.VisibleItems);
        int cursorBefore = left.CursorIndex;

        UiInputResult routed = services.Composition.DispatchInput(new MouseConsoleInputEvent(
            hit.Bounds.X, hit.Bounds.Y, MouseButton.None, MouseEventKind.Move, MouseKeyModifiers.None));

        Assert.True(routed.Handled);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));
        Assert.NotNull(services.Composition.NextHoverWakeUtc);
        Assert.Equal(cursorBefore, left.CursorIndex);
    }

    [Fact]
    public void PartialRender_CommandLineCursorPublishesInteractionWithoutDrawingText()
    {
        var services = Services();
        services.Session.CommandLine.State.SetText("abc");
        services.Composition.Render();
        ApplicationRenderFingerprint previousFingerprint =
            services.ApplicationSurface.CommittedFrame.Fingerprint!;
        UiCursorPlacement previous =
            services.ApplicationSurface.CommittedFrame.CommandLine.Cursor!.Value;
        services.Driver.ClearRecordedOperations();

        services.Session.CommandLine.State.MoveCursor(-1);
        services.ApplicationSurface.RequestRender(ApplicationRenderPart.CommandLine);
        services.Composition.Render();

        ApplicationUiFrame committed = services.ApplicationSurface.CommittedFrame;
        UiCursorPlacement current = committed.CommandLine.Cursor!.Value;
        UiFocusEntry focus = Assert.Single(
            services.ApplicationSurface.CommittedInteractionFrame.Focus.Entries,
            entry => entry.Target == ApplicationTargetIds.CommandLine);
        Assert.True(
            committed.RenderedParts.HasFlag(ApplicationRenderPart.CommandLineCursor),
            $"Cursor-only render was not selected. " +
            $"RenderedParts={committed.RenderedParts}; " +
            $"Viewport={committed.Viewport}; " +
            $"PreviousCommandLine={previousFingerprint.CommandLine}; " +
            $"CurrentCommandLine={committed.Fingerprint!.CommandLine}; " +
            $"PreviousCursor={previous}; CurrentCursor={current}.");
        Assert.False(committed.RenderedParts.HasFlag(ApplicationRenderPart.CommandLine));
        Assert.False(committed.RenderedParts.HasFlag(ApplicationRenderPart.Full));
        Assert.NotEqual(previous.X, current.X);
        Assert.Equal(current, focus.Cursor);
        Assert.DoesNotContain(
            services.Driver.WriteRecords,
            write => write.Y == committed.CommandLine.Bounds.Y);
    }

    [Fact]
    public void PartialRender_CommandLinePublishesKeyboardOwnershipSnapshot()
    {
        var services = Services();
        services.Composition.Render();

        services.Session.CommandLine.State.SetText("abc");
        services.ApplicationSurface.RequestRender(ApplicationRenderPart.CommandLine);
        services.Composition.Render();

        Assert.True(services.ApplicationSurface.CommittedFrame.Keyboard.CommandLineHasText);
        Assert.False(services.ApplicationSurface.CommittedFrame.Keyboard.CommandLineHasSelection);

        services.Session.CommandLine.State.SelectAll();
        services.ApplicationSurface.RequestRender(ApplicationRenderPart.CommandLine);
        services.Composition.Render();

        Assert.True(services.ApplicationSurface.CommittedFrame.Keyboard.CommandLineHasSelection);
    }

    [Fact]
    public void PartialRender_FunctionKeyBarDoesNotDrawPanelsOrCommandLine()
    {
        var services = Services();
        services.Composition.Render();
        services.Driver.ClearRecordedOperations();
        services.Session.FunctionKeyLayer = CSharpFar.App.FunctionKeys.FunctionKeyLayer.Control;

        services.ApplicationSurface.RequestRender(ApplicationRenderPart.FunctionKeyBar);
        services.Composition.Render();

        ApplicationUiFrame committed = services.ApplicationSurface.CommittedFrame;
        Assert.True(committed.RenderedParts.HasFlag(ApplicationRenderPart.FunctionKeyBar));
        Assert.False(committed.RenderedParts.HasFlag(ApplicationRenderPart.Full));
        Assert.Contains(
            committed.FunctionKeyBar!.Actions,
            action =>
                action.Layer == CSharpFar.App.FunctionKeys.FunctionKeyLayer.Control &&
                action.Bounds.Width > 0);
        Assert.All(
            services.Driver.WriteRecords,
            write => Assert.True(write.Y is 0 or 24));
    }

    [Fact]
    public void PartialRender_ClockUsesLogicalApplicationPart()
    {
        var services = Services();
        services.Composition.Render();
        services.Driver.ClearRecordedOperations();

        services.ApplicationSurface.RequestRender(ApplicationRenderPart.Clock);
        services.Composition.Render();

        ApplicationUiFrame committed = services.ApplicationSurface.CommittedFrame;
        Assert.Equal(ApplicationRenderPart.Clock, committed.RenderedParts);
        Assert.NotNull(committed.Fingerprint!.Clock);
        Assert.All(services.Driver.WriteRecords, write => Assert.Equal(0, write.Y));
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
        Assert.Equal(ApplicationTargetIds.WorkspaceKeyboard, routed.Target);
        Assert.Equal(UiInputRouteKind.KeyboardTarget, routed.RouteKind);
    }

    private static TestServices Services(FakeConsoleDriver? driver = null)
    {
        driver ??= new FakeConsoleDriver(80, 25);
        var fs = new FakeFileSystemService();
        string root = OperatingSystem.IsWindows() ? @"C:\Root" : "/Root";
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
        Assert.Null(down.PointerInteraction);
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
}
