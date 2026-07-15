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
    public void LeftDown_UsesCommittedFrameClearsSelectionResetsHistoryAndRequestsRender()
    {
        int fallbackExecutions = 0;
        int historyResets = 0;
        var commandLine = new CommandLineState();
        commandLine.SetText(new string('x', 50));
        commandLine.SelectAll();
        var context = Context(
            commandLine,
            execute: (_, _) =>
            {
                fallbackExecutions++;
                return true;
            },
            resetHistory: () => historyResets++);
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
        Assert.False(commandLine.HasSelection);
        Assert.Equal(1, historyResets);
    }

    [Fact]
    public void CapturedMove_UsesCommittedDisplayOffsetClampsOutsideBoundsAndRequestsRender()
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

        result = handler.Handle(
            new MouseConsoleInputEvent(-100, 10, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.CapturedTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal(9, commandLine.CursorPosition);
        Assert.True(commandLine.HasSelection);
    }

    [Fact]
    public void CapturedLeftUp_IsHandledWithoutRenderOrFallback()
    {
        int fallbackExecutions = 0;
        var commandLine = new CommandLineState();
        var context = Context(commandLine, execute: (_, _) =>
        {
            fallbackExecutions++;
            return true;
        });
        var dispatcher = Dispatcher(context);
        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(5, 24, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None),
            Frame(commandLine),
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.CapturedTarget));

        Assert.False(request.ShouldRender);
        Assert.Equal(0, fallbackExecutions);
    }

    [Theory]
    [InlineData(MouseButton.Right, MouseEventKind.Down)]
    [InlineData(MouseButton.Middle, MouseEventKind.Down)]
    [InlineData(MouseButton.WheelUp, MouseEventKind.Wheel)]
    [InlineData(MouseButton.Left, MouseEventKind.Move)]
    [InlineData(MouseButton.Right, MouseEventKind.Up)]
    public void CapturedCommandLineInput_DoesNotReachFallback(MouseButton button, MouseEventKind kind)
    {
        int fallbackExecutions = 0;
        var commandLine = new CommandLineState();
        var context = Context(commandLine, execute: (_, _) =>
        {
            fallbackExecutions++;
            return true;
        });
        var dispatcher = Dispatcher(context);
        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(5, 24, button, kind, MouseKeyModifiers.None),
            Frame(commandLine),
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.CapturedTarget));

        Assert.Equal(button == MouseButton.Left && kind == MouseEventKind.Move, request.ShouldRender);
        Assert.Equal(0, fallbackExecutions);
    }

    [Fact]
    public void DoubleClick_SelectsWordAndSpaceAfterWordSelectsPreviousWord()
    {
        int historyResets = 0;
        var commandLine = new CommandLineState();
        commandLine.SetText("alpha beta");
        var context = Context(commandLine, resetHistory: () => historyResets++);
        var handler = new ApplicationCommandLineInputHandler(context);
        var frame = new ApplicationCommandLineFrame(
            new Rect(0, 24, 80, 1),
            PromptLength: 3,
            DisplayOffset: 0,
            TextLength: commandLine.Text.Length,
            Cursor: new UiCursorPlacement(3, 24));

        ApplicationInputHandlingResult result = handler.Handle(
            new MouseConsoleInputEvent(10, 24, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal("beta", commandLine.SelectedText);

        result = handler.Handle(
            new MouseConsoleInputEvent(8, 24, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal("alpha", commandLine.SelectedText);
        Assert.Equal(2, historyResets);
    }

    [Fact]
    public void DoubleClick_OnEmptyCommandLineIsSafe()
    {
        int historyResets = 0;
        var commandLine = new CommandLineState();
        var context = Context(commandLine, resetHistory: () => historyResets++);
        var handler = new ApplicationCommandLineInputHandler(context);

        ApplicationInputHandlingResult result = handler.Handle(
            new MouseConsoleInputEvent(3, 24, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            Frame(commandLine).CommandLine,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.False(commandLine.HasSelection);
        Assert.Equal(1, historyResets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightDown_InvokesPasteOnceSkipsFallbackAndMirrorsRenderRequest(bool pasteResult)
    {
        int pasteCalls = 0;
        int fallbackExecutions = 0;
        var commandLine = new CommandLineState();
        var context = Context(
            commandLine,
            execute: (_, _) =>
            {
                fallbackExecutions++;
                return true;
            },
            paste: () =>
            {
                pasteCalls++;
                return pasteResult;
            });
        var dispatcher = Dispatcher(context);

        ApplicationRuntimeRenderRequest request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(5, 24, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None),
            Frame(commandLine),
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.HitTarget));

        Assert.Equal(pasteResult, request.ShouldRender);
        Assert.Equal(1, pasteCalls);
        Assert.Equal(0, fallbackExecutions);
    }

    private static MouseInputContext Context(
        CommandLineState commandLine,
        Func<string, object?, bool>? execute = null,
        Func<bool>? paste = null,
        Action? resetHistory = null)
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
            PasteTextIntoCommandLine = paste ?? (() => true),
            ResetCommandHistoryNavigation = resetHistory ?? (() => { }),
            HideCommandCompletion = _ => { },
            SafeRefresh = (_, _) => { },
            OpenPanelItem = (_, _, _) => { },
        };
    }

    private static ApplicationInputDispatcher Dispatcher(MouseInputContext context) =>
        new(
            _ => ApplicationRuntimeRenderRequest.None,
            _ => ApplicationRuntimeRenderRequest.None,
            new MouseInputRouter(context),
            new ApplicationCommandLineInputHandler(context));

    private static ApplicationUiFrame Frame(CommandLineState commandLine) =>
        new(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationSurfaceMode.Panels,
            new ApplicationCommandLineFrame(
                new Rect(0, 24, 120, 1),
                PromptLength: 8,
                DisplayOffset: 0,
                TextLength: commandLine.Text.Length,
                Cursor: new UiCursorPlacement(8, 24)),
            null,
            null);
}
