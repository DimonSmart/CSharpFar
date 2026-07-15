using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class MainFunctionKeyBarIntegrationTests
{
    [Fact]
    public void MouseClickOnActiveSlot_ExecutesRegisteredCommand()
    {
        int executions = 0;
        var router = CreateRouter(
            canExecute: _ => true,
            execute: (commandId, _) =>
            {
                if (commandId == "copy")
                    executions++;
                return true;
            });

        bool handled = router.Handle(Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down));

        Assert.True(handled);
        Assert.Equal(1, executions);
    }

    [Fact]
    public void MouseClickOnEmptySlot_DoesNothing()
    {
        int executions = 0;
        var router = CreateRouter(canExecute: _ => true, execute: (_, _) =>
        {
            executions++;
            return true;
        });

        bool handled = router.Handle(Mouse(x: 90, y: 24, MouseButton.Left, MouseEventKind.Down));

        Assert.False(handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void MouseClickOnDisabledCommand_DoesNothing()
    {
        int executions = 0;
        var router = CreateRouter(canExecute: _ => false, execute: (_, _) =>
        {
            executions++;
            return true;
        });

        bool handled = router.Handle(Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Down));

        Assert.False(handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void MouseClickOutsideBottomRow_IsNotHandledByFunctionKeyBar()
    {
        int executions = 0;
        var router = CreateRouter(canExecute: _ => true, execute: (_, _) =>
        {
            executions++;
            return true;
        });

        bool handled = router.Handle(Mouse(x: 40, y: 22, MouseButton.Left, MouseEventKind.Down));

        Assert.False(handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void RightClick_IsNotHandledByFunctionKeyBar()
    {
        int executions = 0;
        var router = CreateRouter(canExecute: _ => true, execute: (_, _) =>
        {
            executions++;
            return true;
        });

        bool handled = router.Handle(Mouse(x: 40, y: 24, MouseButton.Right, MouseEventKind.Down));

        Assert.False(handled);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void NonClickMouseEvent_DoesNotExecuteCommand()
    {
        int executions = 0;
        var router = CreateRouter(canExecute: _ => true, execute: (_, _) =>
        {
            executions++;
            return true;
        });

        bool handled = router.Handle(Mouse(x: 40, y: 24, MouseButton.Left, MouseEventKind.Up));

        Assert.False(handled);
        Assert.Equal(0, executions);
    }

    private static MouseInputRouter CreateRouter(
        Func<string, bool> canExecute,
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
            FunctionKeyBindings =
            [
                new FunctionKeyBinding("copy", FunctionKeyLayer.Plain, ConsoleKey.F5, "Copy"),
            ],
            FunctionKeyLayer = () => FunctionKeyLayer.Plain,
            DirectoryShortcuts = () => new AppSettings.DirectoryShortcutSettings(),
            PanelOptions = () => new AppSettings.PanelOptionsSettings(),
            LastRenderSizeOrCurrent = () => new ConsoleSize(120, 25),
            ActiveSide = () => PanelSide.Left,
            SetActiveSide = _ => { },
            ActiveState = () => panelState,
            GetPanelState = _ => panelState,
            ViewModeForSide = _ => PanelViewMode.Full,
            IsPanelVisible = _ => false,
            HasVisiblePanels = () => false,
            QuickView = () => false,
            VisibleRowsForSide = _ => 10,
            ExecuteRegisteredCommand = execute,
            CanExecuteFunctionKeyCommand = canExecute,
            PasteTextIntoCommandLine = () => false,
            ResetCommandHistoryNavigation = () => { },
            HideCommandCompletion = _ => { },
            SafeRefresh = (_, _) => { },
            OpenPanelItem = (_, _, _) => { },
        };

        return new MouseInputRouter(context);
    }

    private static MouseConsoleInputEvent Mouse(int x, int y, MouseButton button, MouseEventKind kind) =>
        new(x, y, button, kind, MouseKeyModifiers.None);
}
