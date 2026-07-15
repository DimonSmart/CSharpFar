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
    public void InputPolicy_TracksPanelVisibilityAndOpenMenuState()
    {
        var fixture = Fixture.Create();

        Assert.Equal(UiLayerInputPolicy.Bubble, fixture.Services.TopMenuLayer.InputPolicy);
        fixture.Services.Composition.Render();
        fixture.Services.Composition.DispatchInput(Key(ConsoleKey.F9));
        Assert.Equal(UiLayerInputPolicy.Modal, fixture.Services.TopMenuLayer.InputPolicy);

        fixture.Services.Session.App.HiddenPanels = HiddenPanels.Both;
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

    private static KeyConsoleInputEvent Key(ConsoleKey key, char keyChar = '\0') =>
        new(new ConsoleKeyInfo(keyChar, key, false, false, false));

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
