using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TopMenuLayerTests
{
    [Fact]
    public void InputPolicy_TracksWorkspaceModeAndOpenMenuState()
    {
        var fixture = Fixture.Create();

        Assert.Equal(UiLayerInputPolicy.Bubble, fixture.Services.TopMenuLayer.InputPolicy);
        fixture.Services.Composition.Render();
        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        Assert.Equal(UiLayerInputPolicy.Modal, fixture.Services.TopMenuLayer.InputPolicy);

        fixture.Services.Session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;
        Assert.Equal(UiLayerInputPolicy.None, fixture.Services.TopMenuLayer.InputPolicy);
    }

    [Fact]
    public void OpenMenu_PublishesOneEnabledCursorlessFocusTarget()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();
        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        fixture.Services.Composition.Render();

        UiFocusFrame focus = fixture.Services.TopMenuLayer.CommittedInteractionFrame.Focus;

        var entry = Assert.Single(focus.Entries);
        Assert.True(entry.IsEnabled);
        Assert.Null(entry.Cursor);
        Assert.Equal(entry.Target, focus.DefaultTarget);
        Assert.Contains("application.top-menu.", entry.Target.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenMenu_IsModalAndBlocksInputFromApplicationSurface()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();
        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        fixture.Services.Composition.Render();

        UiInputResult result = fixture.Services.Composition.DispatchInput(Key(ConsoleKey.A, 'a'));

        Assert.True(result.Handled);
        Assert.False(fixture.Services.ApplicationSurface.TryTakeInput(out _));
    }

    [Fact]
    public void KeyboardNavigation_UpdatesSelectionAndCommittedFocus()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        Assert.Equal(MenuOpenState.DropdownOpen, fixture.Services.Session.Menu.State.OpenState);
        fixture.Services.Composition.Render();

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.LeftArrow));
        Assert.Equal(1, fixture.Services.Session.Menu.State.ActiveTopMenuIndex);
        Assert.Equal(0, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.DownArrow));
        Assert.Equal(3, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.Home));
        Assert.Equal(0, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.End));
        Assert.Equal(4, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.LeftArrow));
        Assert.Equal(0, fixture.Services.Session.Menu.State.ActiveTopMenuIndex);
        Assert.Equal(0, fixture.Services.Session.Menu.State.ActiveDropdownItemIndex);

        fixture.Services.Composition.Render();

        UiFocusFrame focus = fixture.Services.TopMenuLayer.CommittedInteractionFrame.Focus;
        var entry = Assert.Single(focus.Entries);
        Assert.Contains("application.top-menu.dropdown:File:0", entry.Target.Value, StringComparison.Ordinal);
        Assert.Null(entry.Cursor);
    }

    [Fact]
    public void ClosedMenu_TopItemTargetOverridesActivationTargetAndOpensThatItem()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();

        TopMenuPointerTarget topTarget = fixture.Services.TopMenuLayer.CommittedFrame.PointerTargets.Single(
            target => target.Action.Kind == TopMenuPointerActionKind.OpenTopItem && target.Action.ItemIndex == 1);

        Assert.True(fixture.Services.TopMenuLayer.CommittedInteractionFrame.TryHitTest(
            topTarget.Bounds.X,
            topTarget.Bounds.Y,
            out UiHitRegion hit));
        Assert.Equal(topTarget.Target, hit.Target);

        fixture.Services.Composition.DispatchInput(Mouse(topTarget.Bounds.X, topTarget.Bounds.Y));

        Assert.Equal(MenuOpenState.DropdownOpen, fixture.Services.Session.Menu.State.OpenState);
        Assert.Equal(1, fixture.Services.Session.Menu.State.ActiveTopMenuIndex);
    }

    [Fact]
    public void ClosedMenu_ActivationTargetOpensMenuForActivePanel()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();
        TopMenuFrame frame = fixture.Services.TopMenuLayer.CommittedFrame;
        int x = Enumerable.Range(frame.ActivationBounds.X, frame.ActivationBounds.Width)
            .First(value => !frame.PointerTargets.Any(target =>
                target.Action.Kind == TopMenuPointerActionKind.OpenTopItem &&
                target.Bounds.Contains(value, frame.ActivationBounds.Y)));

        fixture.Services.Composition.DispatchInput(Mouse(x, frame.ActivationBounds.Y));

        Assert.Equal(MenuOpenState.DropdownOpen, fixture.Services.Session.Menu.State.OpenState);
        string panelMenuId = frame.ActivePanelSide == PanelSide.Left ? "Left" : "Right";
        Assert.Equal(
            frame.Definition.Items.ToList().FindIndex(item => item.Id == panelMenuId),
            fixture.Services.Session.Menu.State.ActiveTopMenuIndex);
    }

    [Fact]
    public void OpenMenu_DropdownSurfaceConsumesClickAndOutsideLeftClickCloses()
    {
        var fixture = Fixture.Create();
        fixture.Services.Composition.Render();
        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        fixture.Services.Composition.Render();

        TopMenuPointerTarget surface = fixture.Services.TopMenuLayer.CommittedFrame.PointerTargets.Single(
            target => target.Action.Kind == TopMenuPointerActionKind.ConsumeDropdownSurface);
        fixture.Services.Composition.DispatchInput(Mouse(surface.Bounds.X, surface.Bounds.Y));
        Assert.Equal(MenuOpenState.DropdownOpen, fixture.Services.Session.Menu.State.OpenState);

        fixture.Services.Composition.DispatchInput(Mouse(79, 24));
        Assert.Equal(MenuOpenState.Closed, fixture.Services.Session.Menu.State.OpenState);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key, char keyChar = '\0') =>
        new(new ConsoleKeyInfo(keyChar, key, false, false, false));

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private sealed record Fixture(ApplicationServices Services)
    {
        public static Fixture Create()
        {
            var driver = new FakeConsoleDriver(80, 25);
            var fileSystem = new FakeFileSystemService();
            const string root = @"C:\Root";
            fileSystem.AddDirectory(root);
            var settings = new AppSettings();
            settings.Panels.LeftStartDirectory = root;
            settings.Panels.RightStartDirectory = root;
            var services = ApplicationServicesBuilder.Create(
                new ScreenRenderer(driver), fileSystem, new NoOpShellService(),
                new NoOpFileOperationService(), new InMemoryHistoryStore(), settings,
                enableBuiltInNetworkModules: false);
            _ = new Application(services);
            return new Fixture(services);
        }
    }
}
