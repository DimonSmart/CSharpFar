using CSharpFar.App;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ApplicationInputDispatcherTests
{
    [Fact]
    public void CommandLineTargetMouse_DoesNotRunLegacyFallback()
    {
        int fallbackExecutions = 0;
        var commandLine = new CommandLineState();
        commandLine.SetText(new string('x', 50));
        var context = Context(
            commandLine,
            execute: (_, _) =>
            {
                fallbackExecutions++;
                return true;
            });
        var dispatcher = new ApplicationInputDispatcher(
            _ => ApplicationRuntimeRenderRequest.None,
            _ => ApplicationRuntimeRenderRequest.None,
            new MouseInputRouter(context),
            new ApplicationCommandLineInputHandler(context));
        var frame = new ApplicationUiFrame(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationSurfaceMode.Panels,
            new ApplicationCommandLineFrame(new Rect(0, 24, 120, 1), 8, 0, commandLine.Text.Length, new UiCursorPlacement(8, 24)),
            null,
            null);

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(40, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.HitTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(0, fallbackExecutions);
        Assert.Equal(32, commandLine.CursorPosition);
    }

    [Fact]
    public void CapturedMove_UsesCommittedDisplayOffsetAndClampsOutsideBounds()
    {
        var commandLine = new CommandLineState();
        commandLine.SetText("abcdefghijklmnopqrstuvwxyz");
        commandLine.MoveCursorTo(10);
        var context = Context(commandLine);
        var handler = new ApplicationCommandLineInputHandler(context);
        var frame = new ApplicationCommandLineFrame(
            new Rect(0, 10, 8, 1),
            PromptLength: 3,
            DisplayOffset: 12,
            TextLength: commandLine.Text.Length,
            Cursor: new UiCursorPlacement(1, 10));

        ApplicationInputHandlingResult result = handler.Handle(
            new MouseConsoleInputEvent(99, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.CapturedTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal(16, commandLine.CursorPosition);
        Assert.True(commandLine.HasSelection);
    }

    private static MouseInputContext Context(
        CommandLineState commandLine,
        Func<string, object?, bool>? execute = null)
    {
        var panelState = new FilePanelState { CurrentDirectory = @"C:\work" };
        return new MouseInputContext
        {
            PanelController = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService())),
            CommandLine = commandLine,
            Ui = new UiTransientState(),
            Mouse = new MouseSessionState(),
            FunctionKeyBindings =
            [
                new FunctionKeyBinding("copy", FunctionKeyLayer.Plain, ConsoleKey.F5, "Copy"),
            ],
            FunctionKeyLayer = () => FunctionKeyLayer.Plain,
            DirectoryShortcuts = () => new AppSettings.DirectoryShortcutSettings(),
            PanelOptions = () => new AppSettings.PanelOptionsSettings(),
            ActiveSide = () => PanelSide.Left,
            SetActiveSide = _ => { },
            ActiveState = () => panelState,
            GetPanelState = _ => panelState,
            ViewModeForSide = _ => PanelViewMode.Full,
            IsPanelVisible = _ => false,
            HasVisiblePanels = () => false,
            QuickView = () => false,
            VisibleRowsForSide = _ => 10,
            ExecuteRegisteredCommand = execute ?? ((_, _) => false),
            CanExecuteFunctionKeyCommand = _ => true,
            PasteTextIntoCommandLine = () => true,
            ResetCommandHistoryNavigation = () => { },
            HideCommandCompletion = _ => { },
            SafeRefresh = (_, _) => { },
            OpenPanelItem = (_, _, _) => { },
        };
    }
}
