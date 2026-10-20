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
    public void PanelShiftClick_SelectsAdditivelyAndUsesCurrentCursorForEveryRange()
    {
        var state = PanelStateWithItems(7);
        state.CursorIndex = 2;
        state.SelectedPaths.Add(state.Items[0].FullPath);
        state.SelectedLocations.Add(state.Items[0].Location);
        var context = Context(new CommandLineState(), panelState: state);
        context.Mouse.LastLeftPanelItemClick =
            new PanelItemClick(PanelSide.Left, 1, state.Items[1].Location);
        var handler = new ApplicationPanelInputHandler(context);
        var frame = RangeFrame(state);

        Assert.True(handler.Handle(
            RangeMouse(5, MouseEventKind.Down, MouseKeyModifiers.Shift),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 5)).Handled);

        Assert.Equal(5, state.CursorIndex);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
        Assert.True(state.SelectedPaths.SetEquals(
            state.Items.Where((_, i) => i == 0 || i >= 2 && i <= 5).Select(item => item.FullPath)));

        handler.Handle(
            RangeMouse(3, MouseEventKind.Down, MouseKeyModifiers.Shift),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 3));

        Assert.Equal(3, state.CursorIndex);
        Assert.Equal(5, state.SelectedPaths.Count);
        Assert.Equal(5, state.SelectedLocations.Count);
    }

    [Fact]
    public void PanelShiftDoubleClick_NeverOpensAndOrdinaryDoubleClickStillOpens()
    {
        var state = PanelStateWithItems(6);
        state.CursorIndex = 1;
        int opens = 0;
        var context = Context(new CommandLineState(), panelState: state,
            openPanelItem: (_, _, _) => opens++);
        var handler = new ApplicationPanelInputHandler(context);
        var frame = RangeFrame(state);
        UiTargetId target = ApplicationTargetIds.PanelItem(PanelSide.Left, 4);

        handler.Handle(RangeMouse(4, MouseEventKind.Down, MouseKeyModifiers.Shift),
            frame, UiInputRouteKind.HitTarget, target);
        handler.Handle(RangeMouse(4, MouseEventKind.DoubleClick, MouseKeyModifiers.Shift),
            frame, UiInputRouteKind.HitTarget, target);

        Assert.Equal(0, opens);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
        Assert.Equal(4, state.SelectedPaths.Count);

        handler.Handle(RangeMouse(4, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, UiInputRouteKind.HitTarget, target);
        handler.Handle(RangeMouse(4, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame, UiInputRouteKind.HitTarget, target);

        Assert.Equal(1, opens);
        Assert.Equal(4, state.SelectedPaths.Count);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
    }

    [Theory]
    [InlineData(MouseKeyModifiers.None)]
    [InlineData(MouseKeyModifiers.Control)]
    [InlineData(MouseKeyModifiers.Alt)]
    [InlineData(MouseKeyModifiers.Control | MouseKeyModifiers.Shift)]
    [InlineData(MouseKeyModifiers.Alt | MouseKeyModifiers.Shift)]
    public void PanelClick_OtherModifiersPreservePlainCursorBehavior(MouseKeyModifiers modifiers)
    {
        var state = PanelStateWithItems(6);
        state.CursorIndex = 1;
        state.SelectedPaths.Add(state.Items[0].FullPath);
        var context = Context(new CommandLineState(), panelState: state);
        var handler = new ApplicationPanelInputHandler(context);

        handler.Handle(RangeMouse(4, MouseEventKind.Down, modifiers),
            RangeFrame(state),
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 4));

        Assert.Equal(4, state.CursorIndex);
        Assert.Single(state.SelectedPaths);
        Assert.Contains(state.Items[0].FullPath, state.SelectedPaths);
    }

    [Fact]
    public void PanelShiftClick_StaleHitIsRejectedWithoutMovingCursor()
    {
        var state = PanelStateWithItems(6);
        state.CursorIndex = 2;
        var frame = RangeFrame(state);
        var context = Context(new CommandLineState(), panelState: state);
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(PanelSide.Left, 0, state.Items[0].Location);
        state.Items[4] = state.Items[0];

        new ApplicationPanelInputHandler(context).Handle(
            RangeMouse(4, MouseEventKind.Down, MouseKeyModifiers.Shift),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 4));

        Assert.Equal(2, state.CursorIndex);
        Assert.Empty(state.SelectedPaths);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
    }

    [Fact]
    public void PanelShiftClick_InvalidAnchorMovesCursorWithoutSelecting()
    {
        var state = PanelStateWithItems(6);
        state.CursorIndex = -1;
        var context = Context(new CommandLineState(), panelState: state);

        new ApplicationPanelInputHandler(context).Handle(
            RangeMouse(4, MouseEventKind.Down, MouseKeyModifiers.Shift),
            RangeFrame(state),
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, 4));

        Assert.Equal(4, state.CursorIndex);
        Assert.Empty(state.SelectedPaths);
        Assert.Empty(state.SelectedLocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelBriefShiftClick_UsesLogicalIndexAcrossColumns(bool reverse)
    {
        var state = PanelStateWithItems(30);
        var bounds = new Rect(0, 0, 40, 10);
        ApplicationPanelFrame frame = UiTestRender.Render(
            new ScreenRenderer(new FakeConsoleDriver(80, 25)),
            canvas => new BriefTwoColumnsPanelRenderer(canvas, CSharpFarPaletteRegistry.Default)
                .Render(bounds, state, true, PanelSide.Left));
        int secondColumn = frame.RowsPerColumn + 1;
        int anchor = reverse ? secondColumn : 1;
        int target = reverse ? 1 : secondColumn;
        state.CursorIndex = anchor;
        var hit = frame.VisibleItems.Single(item => item.ItemIndex == target);
        var context = Context(new CommandLineState(), panelState: state);

        new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(hit.Bounds.X, hit.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.Shift),
            frame,
            UiInputRouteKind.HitTarget,
            ApplicationTargetIds.PanelItem(PanelSide.Left, target));

        Assert.Equal(target, state.CursorIndex);
        Assert.True(state.SelectedPaths.SetEquals(
            state.Items.Skip(Math.Min(anchor, target)).Take(Math.Abs(anchor - target) + 1)
                .Select(item => item.FullPath)));
    }

}
