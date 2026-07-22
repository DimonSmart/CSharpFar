using CSharpFar.App.Commands;
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

public sealed class MainFunctionKeyBarIntegrationTests
{
    [Fact]
    public void Renderer_PublishesActionBoundsFromSharedLayoutOnlyForExecutableActions()
    {
        var driver = new FakeConsoleDriver(122, 25);
        FunctionKeyBinding[] bindings =
        [
            new("copy", FunctionKeyLayer.Plain, ConsoleKey.F5, "Copy"),
            new("quit", FunctionKeyLayer.Plain, ConsoleKey.F10, "Quit"),
        ];
        var renderer = new ApplicationFunctionKeyBarRenderer(
            bindings,
            commandId => commandId == "copy");

        var frame = UiTestRender.Render(new ScreenRenderer(driver), canvas =>
            renderer.Render(canvas, new ConsoleSize(122, 25), FunctionKeyLayer.Plain));

        var hit = Assert.Single(frame!.Actions);
        Assert.Equal("copy", hit.CommandId);
        Assert.Equal(
            FunctionKeyBar.BuildSlots(y: 24, totalWidth: 122)
                .Single(slot => slot.KeyNumber == 5)
                .Bounds,
            hit.Bounds);
    }

    [Fact]
    public void FrameSnapshotsActionList()
    {
        var actions = new List<ApplicationFunctionKeyHit>
        {
            new(new Rect(0, 24, 10, 1), "copy"),
        };
        var frame = new ApplicationFunctionKeyBarFrame(actions);

        actions[0] = new ApplicationFunctionKeyHit(new Rect(10, 24, 10, 1), "quit");

        Assert.Equal("copy", Assert.Single(frame.Actions).CommandId);
    }

    [Fact]
    public void MouseClickOnCommittedActionExecutesCommandWithCommittedPanelInvocation()
    {
        int executions = 0;
        string? receivedCommandId = null;
        object? receivedArgs = null;
        var handler = CreateHandler((commandId, args) =>
        {
            executions++;
            receivedCommandId = commandId;
            receivedArgs = args;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down),
            Frame(),
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(1, executions);
        Assert.Equal("copy", receivedCommandId);
        var invocation = Assert.IsType<ApplicationPanelCommandInvocation>(receivedArgs);
        Assert.Equal(PanelSide.Left, invocation.Side);
        Assert.Equal(17, invocation.VisibleRows);
        Assert.Equal(PanelLocation.Local(@"C:\left"), invocation.ActivePanel.CurrentLocation);
        Assert.Equal(3, invocation.ActivePanel.CurrentItemIndex);
        Assert.Equal(PanelLocation.Local(@"C:\left\active.txt"), invocation.ActivePanel.CurrentItemLocation);
        Assert.Equal("active.txt", invocation.ActivePanel.CurrentItemName);
        Assert.Equal(PanelLocation.Local(@"C:\right"), invocation.PassivePanel.CurrentLocation);
        Assert.Equal(5, invocation.PassivePanel.CurrentItemIndex);
        Assert.Equal(PanelLocation.Local(@"C:\right\passive.txt"), invocation.PassivePanel.CurrentItemLocation);
        Assert.Equal("passive.txt", invocation.PassivePanel.CurrentItemName);
    }

    private static ApplicationFunctionKeyBarInputHandler CreateHandler(
        Func<string, object?, bool> execute)
    {
        var panelState = new FilePanelState { CurrentDirectory = @"C:\work" };
        var panelController = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService()));
        var context = new MouseInputContext
        {
            PanelController = panelController,
            CommandLine = new CommandLineState(),
            Ui = new UiTransientState(),
            Mouse = new MouseSessionState(),
            PanelOptions = () => new AppSettings.PanelOptionsSettings(),
            SetActiveSide = _ => { },
            GetPanelState = _ => panelState,
            ExecuteRegisteredCommand = execute,
            PasteTextIntoCommandLine = () => false,
            ResetCommandHistoryNavigation = () => { },
            SafeRefresh = (_, _) => { },
            OpenPanelItem = (_, _, _) => { },
        };

        return new ApplicationFunctionKeyBarInputHandler(context);
    }

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseButton button, MouseEventKind kind) =>
        new(x, y, button, kind, MouseKeyModifiers.None);

    private static ApplicationUiFrame Frame() =>
        new(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationWorkspaceMode.Panels,
            new ApplicationKeyboardFrame(
                PanelSide.Left,
                false,
                false,
                new ApplicationPanelKeyboardFrame(
                    PanelLocation.Local(@"C:\left"), false, 3, PanelLocation.Local(@"C:\left\active.txt"), "active.txt"),
                new ApplicationPanelKeyboardFrame(
                    PanelLocation.Local(@"C:\right"), false, 5, PanelLocation.Local(@"C:\right\passive.txt"), "passive.txt")),
            new ApplicationCommandLineFrame(new Rect(0, 23, 120, 1), 8, 0, 0, new UiCursorPlacement(8, 23)),
            new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 60, 23), 17, [], null, null),
            new ApplicationPanelFrame(PanelSide.Right, new Rect(60, 0, 60, 23), 19, [], null, null),
            new ApplicationFunctionKeyBarFrame(
                [new ApplicationFunctionKeyHit(new Rect(40, 24, 10, 1), "copy")]),
            null);
}
