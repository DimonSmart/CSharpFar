using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed partial class ApplicationNavigationTests
{
    [Theory]
    [InlineData("CtrlF1", "both")]
    [InlineData("CtrlF2", "both")]
    [InlineData("CtrlF1 CtrlF2", "both")]
    [InlineData("CtrlO CtrlF1", "none")]
    [InlineData("CtrlO CtrlF2", "none")]
    public void HandleKey_ControlF1AndF2DoNotChangeWorkspaceMode(string keys, string expectedPanels)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        Render(app);
        foreach (string key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            HandleKeyAndRender(app, WorkspaceModeKey(key));

        AssertVisiblePanels(driver, expectedPanels);
    }

    [Fact]
    public void HandleKey_ControlF1DoesNotChangeActiveSide()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.F1, control: true));

        Assert.Equal(PanelSide.Left, GetActiveSide(app));
    }

    [Fact]
    public void HandleKey_TabAlwaysSwitchesActiveSideInPanels()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.Tab));

        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }

    [Fact]
    public void HandleKey_CtrlOHideShow_PreservesActiveSide()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        var app = CreateApp(fs, new FakeConsoleDriver(width: 80, height: 12), _tempDir);

        HandleKeyAndRender(app, Key(ConsoleKey.Tab));
        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));

        Assert.Equal(ApplicationWorkspaceMode.HiddenCommandLine, app.Session.App.WorkspaceMode);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));

        Assert.Equal(ApplicationWorkspaceMode.Panels, app.Session.App.WorkspaceMode);
        Assert.Equal(PanelSide.Right, GetActiveSide(app));
    }


    [Fact]
    public void HandleKey_CtrlOResetsModalModifierSnapshotAndCommitsPlainMouseTargets()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        var app = CreateApp(fs, driver, _tempDir);

        Render(app);
        var composition = (UiCompositionHost)(typeof(Application).GetField(
            "_composition", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app) ?? throw new InvalidOperationException("Composition host not found."));
        var dispatcher = (ApplicationInputDispatcher)(typeof(Application).GetField(
            "_applicationInputDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(app) ?? throw new InvalidOperationException("Input dispatcher not found."));
        ApplicationUiSurface surface = GetApplicationSurface(app);

        composition.DispatchInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control));
        Assert.True(surface.TryTakeInput(out var modifierPacket));
        ApplicationRuntimeRenderRequest modifierRequest = dispatcher.Handle(modifierPacket);
        Assert.True(modifierRequest.ShouldRender);
        Assert.Equal(ApplicationRenderPart.FunctionKeyBar, modifierRequest.Parts);
        surface.RequestRender(modifierRequest.Parts);
        Render(app);

        Assert.Equal(ConsoleModifiers.Control, composition.LastObservedModifiers);
        Assert.Equal(FunctionKeyLayer.Control, app.Session.FunctionKeyLayer);
        Assert.All(surface.CommittedFrame.FunctionKeyBar!.Actions,
            action => Assert.Equal(FunctionKeyLayer.Control, action.Layer));

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
        Assert.Equal(default(ConsoleModifiers), composition.LastObservedModifiers);

        HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
        Assert.Equal(default(ConsoleModifiers), composition.LastObservedModifiers);

        ApplicationFunctionKeyBarFrame bar = Assert.IsType<ApplicationFunctionKeyBarFrame>(
            surface.CommittedFrame.FunctionKeyBar);
        Assert.NotEmpty(bar.Actions);
        Assert.All(bar.Actions, action => Assert.Equal(FunctionKeyLayer.Plain, action.Layer));
        ApplicationFunctionKeyHit quit = Assert.Single(bar.Actions, action => action.Key == ConsoleKey.F10);
        Assert.True(surface.CommittedInteractionFrame.TryHitTest(quit.Bounds.X, quit.Bounds.Y, out var region));
        Assert.Equal(ApplicationTargetIds.FunctionKeyAction(FunctionKeyLayer.Plain, ConsoleKey.F10), region.Target);

        HandleKeyAndRender(app, Key(ConsoleKey.F9));
        HandleKeyAndRender(app, Key(ConsoleKey.Escape));
        Assert.Equal(FunctionKeyLayer.Plain, app.Session.FunctionKeyLayer);
    }

    [Theory]
    [InlineData(41, 2, false, false, true, PanelSide.Right, 0, 1)]
    [InlineData(40, 2, false, false, true, PanelSide.Right, 0, 1)]
    [InlineData(1, 2, false, true, true, PanelSide.Left, 1, 0)]
    [InlineData(41, 2, false, true, false, PanelSide.Left, 0, 0)]
    public void HandleMouse_ClickPanelArea_UpdatesPanelSelection(
        int x,
        int y,
        bool hiddenCommandLine,
        bool quickView,
        bool expectedHandled,
        PanelSide expectedActiveSide,
        int expectedLeftCursor,
        int expectedRightCursor)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "first.txt", FullPath = Path.Combine(_tempDir, "first.txt"), IsDirectory = false },
            new FilePanelItem { Name = "second.txt", FullPath = Path.Combine(_tempDir, "second.txt"), IsDirectory = false });

        var driver = new FakeConsoleDriver(width: 80, height: 12);
        var app = CreateApp(fs, driver, _tempDir);
        app.QuickView = quickView;
        if (hiddenCommandLine)
            HandleKeyAndRender(app, Key(ConsoleKey.O, keyChar: '\u000f', control: true));
        Render(app);

        bool handled = HandleMouse(app, LeftMouse(x, y, MouseEventKind.Down));

        Assert.Equal(expectedHandled, handled);
        Assert.Equal(expectedActiveSide, GetActiveSide(app));
        Assert.Equal(expectedLeftCursor, GetLeftPanel(app).CursorIndex);
        Assert.Equal(expectedRightCursor, GetRightPanel(app).CursorIndex);
    }

}
