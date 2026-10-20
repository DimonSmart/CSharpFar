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
            services.ApplicationSurface.CommittedFrame.FunctionKeyBar!.Actions.Where(action => action.Bounds.Width > 0),
            action => Assert.Contains(
                services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
                region => region.Target == ApplicationTargetIds.FunctionKeyAction(action.Layer, action.Key) &&
                          region.Bounds.Equals(action.Bounds)));
        Assert.All(
            services.ApplicationSurface.CommittedFrame.DirectoryShortcutBar!.Shortcuts,
            shortcut => Assert.Contains(
                services.ApplicationSurface.CommittedInteractionFrame.HitRegions,
                region => region.Target == ApplicationTargetIds.DirectoryShortcut(shortcut.ShortcutNumber) &&
                          region.Bounds.Equals(shortcut.Bounds)));
    }

    [Theory]
    [MemberData(nameof(KeyboardRoutedInputs))]
    public void SupportedKeyboardInput_CreatesOneApplicationPacket(ConsoleInputEvent input, Type expectedType)
    {
        var services = Services();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.IsType(expectedType, routed.Input);
        Assert.Equal(ApplicationTargetIds.WorkspaceKeyboard, routed.Target);
        Assert.Equal(UiInputRouteKind.KeyboardTarget, routed.RouteKind);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(Key(ConsoleKey.B));
        Assert.True(services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void PanelPointerInputs_CreateSemanticApplicationPackets()
    {
        var services = Services();
        var input = Mouse();
        services.Composition.Render();

        UiInputResult result = services.Composition.DispatchInput(input);

        Assert.True(result.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var routed));
        Assert.IsType<MouseConsoleInputEvent>(routed.Input);
        Assert.Equal(ApplicationTargetIds.PanelItem(PanelSide.Left, 0), routed.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, routed.RouteKind);
        Assert.False(services.ApplicationSurface.TryTakeInput(out _));

        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(1, 20, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var background));
        Assert.Equal(ApplicationTargetIds.Panel(PanelSide.Left), background.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, background.RouteKind);

        services.Session.Panels.Left.LoadError = new PanelLoadError
        {
            Message = "Test error",
            RetryLocation = services.Session.Panels.Left.CurrentLocation,
        };
        services.Composition.Render();
        Rect retry = Assert.IsType<Rect>(services.ApplicationSurface.CommittedFrame.LeftPanel!.RetryBounds);
        services.Composition.DispatchInput(
            new MouseConsoleInputEvent(retry.X, retry.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        Assert.True(services.ApplicationSurface.TryTakeInput(out var retryPacket));
        Assert.Equal(ApplicationTargetIds.PanelRetry(PanelSide.Left), retryPacket.Target);
        Assert.Equal(UiInputRouteKind.HitTarget, retryPacket.RouteKind);
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

    public static TheoryData<ConsoleInputEvent, Type> KeyboardRoutedInputs() => new()
    {
        { Key(ConsoleKey.A), typeof(KeyConsoleInputEvent) },
        { new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control), typeof(ModifierKeyConsoleInputEvent) },
    };

    [Theory]
    [InlineData(PanelViewMode.Full)]
    [InlineData(PanelViewMode.BriefTwoColumns)]
    public void ShiftClick_ProductionPointerRouteSelectsLogicalRange(PanelViewMode mode)
    {
        var services = Services();
        FilePanelState state = services.Session.Panels.Left;
        AddScrollableItems(state, 70);
        state.CursorIndex = 2;
        state.ScrollOffset = 1;
        services.Session.Panels.LeftViewMode = mode;
        services.Composition.Render();

        ApplicationPanelFrame frame = services.ApplicationSurface.CommittedFrame.LeftPanel!;
        int targetIndex = mode == PanelViewMode.Full
            ? 5
            : state.ScrollOffset + frame.RowsPerColumn + 1;
        ApplicationPanelItemHit target = frame.VisibleItems.Single(hit => hit.ItemIndex == targetIndex);
        UiInputResult routed = services.Composition.DispatchInput(new MouseConsoleInputEvent(
            target.Bounds.X, target.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.Shift));

        Assert.True(routed.Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        Assert.Equal(MouseKeyModifiers.Shift, Assert.IsType<MouseConsoleInputEvent>(packet.Input).Modifiers);
        Assert.Equal(targetIndex, Assert.IsType<ApplicationPanelInteraction>(packet.PointerInteraction).Action.Item!.Item!.ItemIndex);
        Assert.True(services.Inner.ApplicationInputDispatcher.Handle(packet).ShouldRender);
        Assert.Equal(targetIndex, state.CursorIndex);
        Assert.True(state.SelectedPaths.SetEquals(
            state.Items.Skip(2).Take(targetIndex - 1).Select(item => item.FullPath)));
        Assert.True(state.SelectedLocations.SetEquals(
            state.Items.Skip(2).Take(targetIndex - 1).Select(item => item.Location)));
    }

    [Fact]
    public void ShiftClick_ProductionPointerRouteUsesClickedPanelNotActivePanel()
    {
        var services = Services();
        FilePanelState left = services.Session.Panels.Left;
        FilePanelState right = services.Session.Panels.Right;
        AddScrollableItems(left, 12);
        AddScrollableItems(right, 12);
        left.CursorIndex = 4;
        right.CursorIndex = 2;
        left.SelectedPaths.Add(left.Items[0].FullPath);
        left.SelectedLocations.Add(left.Items[0].Location);
        services.Composition.Render();

        ApplicationPanelItemHit target = services.ApplicationSurface.CommittedFrame.RightPanel!
            .VisibleItems.Single(hit => hit.ItemIndex == 7);
        Assert.True(services.Composition.DispatchInput(new MouseConsoleInputEvent(
            target.Bounds.X, target.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.Shift)).Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        services.Inner.ApplicationInputDispatcher.Handle(packet);

        Assert.Equal(PanelSide.Right, services.Session.Panels.ActiveSide);
        Assert.Equal(4, left.CursorIndex);
        Assert.Single(left.SelectedPaths);
        Assert.Equal(7, right.CursorIndex);
        Assert.True(right.SelectedLocations.SetEquals(right.Items.Skip(2).Take(6).Select(item => item.Location)));
    }

    [Fact]
    public void ShiftClick_ProductionPointerRouteRejectsStaleHitIdentity()
    {
        var services = Services();
        FilePanelState state = services.Session.Panels.Left;
        AddScrollableItems(state, 12);
        state.CursorIndex = 1;
        services.Composition.Render();
        ApplicationPanelItemHit target = services.ApplicationSurface.CommittedFrame.LeftPanel!
            .VisibleItems.Single(hit => hit.ItemIndex == 5);
        state.Items[5] = state.Items[0];

        Assert.True(services.Composition.DispatchInput(new MouseConsoleInputEvent(
            target.Bounds.X, target.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.Shift)).Handled);
        Assert.True(services.ApplicationSurface.TryTakeInput(out var packet));
        services.Inner.ApplicationInputDispatcher.Handle(packet);

        Assert.Equal(1, state.CursorIndex);
        Assert.Empty(state.SelectedPaths);
        Assert.Empty(state.SelectedLocations);
    }

}
