using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class ApplicationInputDispatcherTests
{
    [Fact]
    public void UnknownOrLayerMouseTarget_DoesNotChangeApplicationState()
    {
        var state = PanelStateWithItems();
        var commandLine = new CommandLineState();
        int executions = 0;
        var dispatcher = Dispatcher(Context(commandLine, panelState: state, execute: (_, _) => { executions++; return true; }));
        var frame = Frame(commandLine) with
        {
            LeftPanel = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 8,
                [new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"))], null, null),
        };

        Assert.False(dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, new UiTargetId("application.unknown"), UiInputRouteKind.HitTarget)).ShouldRender);
        Assert.False(dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, ApplicationTargetIds.LeftPanel, UiInputRouteKind.Layer)).ShouldRender);
        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void PanelLeftDown_UsesCommittedItemHitSnapshotAfterSourceListMutates()
    {
        var state = PanelStateWithItems();
        var context = Context(new CommandLineState(), panelState: state);
        var handler = new ApplicationPanelInputHandler(context);
        var hits = new List<ApplicationPanelItemHit>
        {
            new(new Rect(1, 1, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt")),
        };
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            hits,
            null,
            null);
        hits[0] = new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"));

        var result = handler.Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 0));

        Assert.True(result.Handled);
        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(new PanelItemClick(PanelSide.Left, 0, PanelLocation.Local(@"C:\work\a.txt")), context.Mouse.LastLeftPanelItemClick);
    }

    [Fact]
    public void PanelDoubleClick_DoesNotOpenDifferentItemAfterReorder()
    {
        var state = PanelStateWithItems();
        (PanelSide Side, FilePanelItem Item)? opened = null;
        var context = Context(
            new CommandLineState(),
            panelState: state,
            openPanelItem: (_, side, item) => opened = (side, item));
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(PanelSide.Left, 0, PanelLocation.Local(@"C:\work\a.txt"));
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            [new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt"))],
            null,
            null);
        (state.Items[0], state.Items[1]) = (state.Items[1], state.Items[0]);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 0));

        Assert.True(result.Handled);
        Assert.Null(opened);
    }

    [Fact]
    public void PanelDoubleClick_DoesNotOpenSamePathFromDifferentProvider()
    {
        var state = PanelStateWithItems();
        state.Items[0] = new FilePanelItem
        {
            Name = "a.txt",
            FullPath = @"C:\work\a.txt",
            SourceId = new PanelSourceId("provider-b"),
            IsDirectory = false,
        };
        (PanelSide Side, FilePanelItem Item)? opened = null;
        var context = Context(new CommandLineState(), panelState: state,
            openPanelItem: (_, side, item) => opened = (side, item));
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(
            PanelSide.Left,
            0,
            new PanelLocation(new PanelSourceId("provider-a"), @"C:\work\a.txt"));
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            [new ApplicationPanelItemHit(
                new Rect(1, 1, 10, 1),
                0,
                new PanelLocation(new PanelSourceId("provider-b"), @"C:\work\a.txt"))],
            null,
            null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 0));

        Assert.True(result.Handled);
        Assert.Null(opened);
    }

    [Theory]
    [InlineData(ScrollBarHitPart.DecreaseButton)]
    [InlineData(ScrollBarHitPart.IncreaseButton)]
    [InlineData(ScrollBarHitPart.TrackBeforeThumb)]
    [InlineData(ScrollBarHitPart.TrackAfterThumb)]
    public void PanelScrollbarClick_UsesCommittedFrameWithoutCreatingDrag(ScrollBarHitPart part)
    {
        var state = PanelStateWithItems(100);
        state.ScrollOffset = 30;
        PanelSide? activeSide = null;
        var context = Context(new CommandLineState(), panelState: state, setActiveSide: side => activeSide = side);
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(PanelSide.Right, 4, PanelLocation.Local("old"));
        var frame = new ApplicationScrollBarFrame(new Rect(10, 10, 1, 20), 100, 10, 30);
        var hit = ScrollBarInteraction.CalculateThumb(frame.Bounds, frame.ToScrollState());
        int y = part switch
        {
            ScrollBarHitPart.DecreaseButton => frame.Bounds.Y,
            ScrollBarHitPart.IncreaseButton => frame.Bounds.Bottom - 1,
            ScrollBarHitPart.TrackBeforeThumb => hit.ThumbY - 1,
            ScrollBarHitPart.TrackAfterThumb => hit.ThumbY + hit.ThumbHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

        int firstVisibleIndex = ScrollBarInteraction.ApplyClick(frame.ToScrollState(), part);
        var result = new ApplicationPanelScrollbarInputHandler(context).Handle(
            new ApplicationPanelScrollInteraction(PanelSide.Left, frame.ViewportItems, firstVisibleIndex));

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal(PanelSide.Left, activeSide);
        Assert.Equal(firstVisibleIndex, state.ScrollOffset);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelRightClick_UsesCommittedHitAndSelectionSetting(bool selectsFiles)
    {
        var state = PanelStateWithItems();
        PanelSide? activeSide = null;
        var options = new AppSettings.PanelOptionsSettings { RightClickSelectsFiles = selectsFiles };
        var context = Context(new CommandLineState(), panelState: state, options: options, setActiveSide: side => activeSide = side);
        var frame = new ApplicationPanelFrame(PanelSide.Right, new Rect(40, 0, 40, 10), 1,
            [new ApplicationPanelItemHit(new Rect(41, 2, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"))], null, null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(41, 2, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Right, 1));

        Assert.True(result.Handled);
        Assert.Equal(PanelSide.Right, activeSide);
        Assert.Equal(1, state.CursorIndex);
        Assert.Equal(selectsFiles, state.SelectedPaths.Contains(@"C:\work\b.txt"));
    }

    [Fact]
    public void PanelRightClick_StaleIdentityDoesNotMoveCursorOrSelection()
    {
        var state = PanelStateWithItems();
        state.CursorIndex = 0;
        var context = Context(new CommandLineState(), panelState: state);
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 8,
            [new ApplicationPanelItemHit(new Rect(1, 2, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt"))], null, null);
        state.Items[0] = state.Items[1];

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 2, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 0));

        Assert.True(result.Handled);
        Assert.Equal(0, state.CursorIndex);
        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void PanelWheel_UsesCommittedVisibleRows()
    {
        var state = PanelStateWithItems(20);
        state.ScrollOffset = 14;
        state.CursorIndex = 14;
        var context = Context(new CommandLineState(), panelState: state);
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 3, [], null, null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.Panel(PanelSide.Left));

        Assert.True(result.Handled);
        Assert.Equal(17, state.ScrollOffset);
    }

    [Fact]
    public void PanelRetry_UsesCommittedBoundsAndVisibleRows()
    {
        var state = PanelStateWithItems();
        int refreshCalls = 0;
        int visibleRows = 0;
        var context = Context(new CommandLineState(), panelState: state, safeRefresh: (_, rows) => { refreshCalls++; visibleRows = rows; });
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 7, [], new Rect(2, 3, 5, 1), null);
        var handler = new ApplicationPanelInputHandler(context);

        Assert.True(handler.Handle(
            new MouseConsoleInputEvent(3, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelRetry(PanelSide.Left)).Handled);
        Assert.True(handler.Handle(
            new MouseConsoleInputEvent(20, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.Panel(PanelSide.Left)).Handled);
        Assert.Equal(1, refreshCalls);
        Assert.Equal(7, visibleRows);
    }

    [Fact]
    public void BriefRenderer_MetadataExcludesSeparatorAndRoutesSecondColumnItem()
    {
        var state = PanelStateWithItems(20);
        var bounds = new Rect(0, 0, 40, 10);
        ApplicationPanelFrame frame = UiTestRender.Render(
            new ScreenRenderer(new FakeConsoleDriver(80, 25)),
            canvas =>
            {
                var renderer = new BriefTwoColumnsPanelRenderer(canvas, CSharpFarPaletteRegistry.Default);
                return renderer.Render(bounds, state, isActive: true, PanelSide.Left);
            });
        Assert.NotEmpty(frame.VisibleItems);

        int separatorX = Enumerable.Range(bounds.X + 1, bounds.Width - 2)
            .Single(x => frame.VisibleItems.All(hit => !hit.Bounds.Contains(x, frame.VisibleItems[0].Bounds.Y)));
        ApplicationPanelItemHit secondColumnItem = frame.VisibleItems
            .First(hit => hit.Bounds.X > separatorX);
        var handler = new ApplicationPanelInputHandler(Context(new CommandLineState(), panelState: state));

        handler.Handle(
            new MouseConsoleInputEvent(separatorX, secondColumnItem.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.Panel(PanelSide.Left));
        Assert.Equal(0, state.CursorIndex);
        handler.Handle(
            new MouseConsoleInputEvent(secondColumnItem.Bounds.X, secondColumnItem.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, secondColumnItem.ItemIndex));
        Assert.Equal(secondColumnItem.ItemIndex, state.CursorIndex);
    }

}
