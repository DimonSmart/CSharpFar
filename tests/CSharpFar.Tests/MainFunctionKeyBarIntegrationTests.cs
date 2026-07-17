using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Commands;
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
            new ScreenRenderer(driver),
            bindings,
            commandId => commandId == "copy");

        var frame = renderer.Render(new ConsoleSize(122, 25), FunctionKeyLayer.Plain);

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
    public void MouseClickOnActiveSlot_ExecutesRegisteredCommand()
    {
        int executions = 0;
        var handler = CreateHandler(
            execute: (commandId, _) =>
            {
                if (commandId == "copy")
                    executions++;
                return true;
            });

        var result = handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(1, executions);
    }

    [Fact]
    public void MouseClickOnActiveSlot_UsesCommittedPanelInvocation()
    {
        object? receivedArgs = null;
        var handler = CreateHandler((_, args) =>
        {
            receivedArgs = args;
            return true;
        });

        handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        var invocation = Assert.IsType<ApplicationPanelCommandInvocation>(receivedArgs);
        Assert.Equal(PanelSide.Left, invocation.Side);
        Assert.Equal(@"C:\left", invocation.ActivePanel.CurrentDirectory);
        Assert.Equal(@"C:\right", invocation.PassivePanel.CurrentDirectory);
    }

    [Fact]
    public void MouseClickOnEmptySlot_DoesNothing()
    {
        int executions = 0;
        var handler = CreateHandler(execute: (_, _) =>
        {
            executions++;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 90, y: 24, MouseButton.Left, MouseEventKind.Down),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        Assert.False(result.Handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void MouseClickOnDisabledCommand_DoesNothing()
    {
        int executions = 0;
        var handler = CreateHandler(execute: (_, _) =>
        {
            executions++;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down),
            Frame(canExecute: false),
            UiInputRouteKind.HitTarget);

        Assert.False(result.Handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void MouseClickOutsideBottomRow_IsNotHandledByFunctionKeyBar()
    {
        int executions = 0;
        var handler = CreateHandler(execute: (_, _) =>
        {
            executions++;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 40, y: 22, MouseButton.Left, MouseEventKind.Down),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        Assert.False(result.Handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void RightClick_IsNotHandledByFunctionKeyBar()
    {
        int executions = 0;
        var handler = CreateHandler(execute: (_, _) =>
        {
            executions++;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Right, MouseEventKind.Down),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        Assert.False(result.Handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void NonClickMouseEvent_DoesNotExecuteCommand()
    {
        int executions = 0;
        var handler = CreateHandler(execute: (_, _) =>
        {
            executions++;
            return true;
        });

        var result = handler.Handle(
            Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Up),
            Frame(canExecute: true),
            UiInputRouteKind.HitTarget);

        Assert.False(result.Handled);
        Assert.Equal(0, executions);
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

    private static ApplicationUiFrame Frame(bool canExecute) =>
        new(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationWorkspaceMode.Panels,
            new ApplicationKeyboardFrame(
                PanelSide.Left,
                false,
                false,
                new ApplicationPanelKeyboardFrame(PanelLocation.Local(@"C:\left"), false, null, null, null),
                new ApplicationPanelKeyboardFrame(PanelLocation.Local(@"C:\right"), false, null, null, null)),
            new ApplicationCommandLineFrame(new Rect(0, 23, 120, 1), 8, 0, 0, new UiCursorPlacement(8, 23)),
            null,
            null,
            canExecute
                ? new ApplicationFunctionKeyBarFrame(
                    [new ApplicationFunctionKeyHit(new Rect(40, 24, 10, 1), "copy")])
                : null,
            null);
}
