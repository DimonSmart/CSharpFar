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
            KeyboardRouter(commandLine),
            new ApplicationCommandLineInputHandler(context),
            new ApplicationPanelInputHandler(context),
            new ApplicationPanelScrollbarInputHandler(context),
            new ApplicationFunctionKeyBarInputHandler(context),
            new ApplicationDirectoryShortcutBarInputHandler(context));
        var frame = new ApplicationUiFrame(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationWorkspaceMode.Panels,
            KeyboardFrame(PanelSide.Left, commandLine.HasText, commandLine.HasSelection),
            new ApplicationCommandLineFrame(new Rect(0, 24, 120, 1), 8, 0, commandLine.Text.Length, new UiCursorPlacement(8, 24)),
            null,
            null,
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

    [Fact]
    public void FunctionKeyAndDirectoryShortcutTargets_ExecuteCommittedActions()
    {
        (string CommandId, object? Args)? executed = null;
        var commandLine = new CommandLineState();
        var context = Context(commandLine, execute: (commandId, args) =>
        {
            executed = (commandId, args);
            return true;
        });
        var dispatcher = Dispatcher(context);
        var frame = Frame(commandLine) with
        {
            FunctionKeyBar = new ApplicationFunctionKeyBarFrame(
                [new ApplicationFunctionKeyHit(
                    new Rect(1, 24, 9, 1),
                    FunctionKeyCommandIds.Search,
                    FunctionKeyLayer.Alt,
                    ConsoleKey.F7)]),
            DirectoryShortcutBar = new ApplicationDirectoryShortcutBarFrame(
                [new ApplicationDirectoryShortcutHit(new Rect(1, 22, 9, 1), 1, @"C:\Rendered")]),
        };

        var functionKeyRequest = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(2, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            ApplicationTargetIds.FunctionKeyAction(FunctionKeyLayer.Alt, ConsoleKey.F7),
            UiInputRouteKind.HitTarget));

        Assert.True(functionKeyRequest.ShouldRender);
        Assert.NotNull(executed);
        Assert.Equal(FunctionKeyCommandIds.Search, executed.Value.CommandId);
        var invocation = Assert.IsType<ApplicationPanelCommandInvocation>(executed.Value.Args);
        Assert.Equal(PanelSide.Left, invocation.Side);

        var shortcutRequest = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(2, 22, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            ApplicationTargetIds.DirectoryShortcut(1),
            UiInputRouteKind.HitTarget));

        Assert.True(shortcutRequest.ShouldRender);
        Assert.NotNull(executed);
        Assert.Equal(DirectoryShortcutCommandIds.Navigate, executed.Value.CommandId);
        var args = Assert.IsType<NavigateToCommittedDirectoryShortcutArgs>(executed.Value.Args);
        Assert.Equal(1, args.Number);
        Assert.Equal(@"C:\Rendered", args.Path);
    }

    [Theory]
    [InlineData(null, UiInputRouteKind.Layer)]
    [InlineData(null, UiInputRouteKind.HitTarget)]
    public void MouseWithoutApplicationTarget_DoesNotInvokeFallback(
        UiTargetId? target,
        UiInputRouteKind routeKind)
    {
        int executions = 0;
        var commandLine = new CommandLineState();
        var context = Context(commandLine, execute: (_, _) =>
        {
            executions++;
            return true;
        });
        var dispatcher = Dispatcher(context);

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(5, 5, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            Frame(commandLine),
            target,
            routeKind));

        Assert.False(request.ShouldRender);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void KeyWithoutWorkspaceKeyboardRoute_DoesNotInvokeKeyboardHandlers()
    {
        var commandLine = new CommandLineState();
        var dispatcher = Dispatcher(Context(commandLine));

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)),
            Frame(commandLine),
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.FocusedTarget));

        Assert.False(request.ShouldRender);
        Assert.Equal(string.Empty, commandLine.Text);
    }

    private static ApplicationPanelFrame RangeFrame(FilePanelState state)
    {
        var hits = state.Items.Select((item, index) =>
            new ApplicationPanelItemHit(new Rect(1, index + 1, 10, 1), index, item.Location)).ToArray();
        return new ApplicationPanelFrame(
            PanelSide.Left, new Rect(0, 0, 40, 10), 8, hits, null, null);
    }

    private static MouseConsoleInputEvent RangeMouse(int index, MouseEventKind kind, MouseKeyModifiers modifiers) =>
        new(1, index + 1, MouseButton.Left, kind, modifiers);

    private static MouseInputContext Context(
        CommandLineState commandLine,
        Func<string, object?, bool>? execute = null,
        Func<bool>? paste = null,
        Action? resetHistory = null,
        FilePanelState? panelState = null,
        Action<FilePanelState, PanelSide, FilePanelItem>? openPanelItem = null,
        AppSettings.PanelOptionsSettings? options = null,
        Action<PanelSide>? setActiveSide = null,
        Action<FilePanelState, int>? safeRefresh = null)
    {
        panelState ??= new FilePanelState { CurrentDirectory = @"C:\work" };
        return new MouseInputContext
        {
            PanelController = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService())),
            CommandLine = commandLine,
            Ui = new UiTransientState(),
            Mouse = new MouseSessionState(),
            PanelOptions = () => options ?? new AppSettings.PanelOptionsSettings(),
            SetActiveSide = setActiveSide ?? (_ => { }),
            GetPanelState = _ => panelState,
            ExecuteRegisteredCommand = execute ?? ((_, _) => false),
            PasteTextIntoCommandLine = paste ?? (() => true),
            ResetCommandHistoryNavigation = resetHistory ?? (() => { }),
            SafeRefresh = safeRefresh ?? ((_, _) => { }),
            OpenPanelItem = openPanelItem ?? ((_, _, _) => { }),
        };
    }

    private static FilePanelState PanelStateWithItems(int count = 2)
    {
        var state = new FilePanelState { CurrentDirectory = @"C:\work" };
        for (int i = 0; i < count; i++)
        {
            string name = i == 0 ? "a.txt" : i == 1 ? "b.txt" : $"item-{i}.txt";
            state.Items.Add(new FilePanelItem
            {
                Name = name,
                FullPath = $@"C:\work\{name}",
                IsDirectory = false,
            });
        }
        return state;
    }

    private static ApplicationInputDispatcher Dispatcher(MouseInputContext context) =>
        new(
            KeyboardRouter(context.CommandLine),
            new ApplicationCommandLineInputHandler(context),
            new ApplicationPanelInputHandler(context),
            new ApplicationPanelScrollbarInputHandler(context),
            new ApplicationFunctionKeyBarInputHandler(context),
            new ApplicationDirectoryShortcutBarInputHandler(context));

    private static KeyboardInputRouter KeyboardRouter(
        CommandLineState commandLine,
        Func<FilePanelState>? leftPanel = null,
        Func<FilePanelState>? rightPanel = null,
        Action<FilePanelState, PanelSide, FilePanelItem>? openPanelItem = null,
        Action<PanelSide>? toggleSelectAllPanelItems = null,
        Func<ApplicationWorkspaceMode, bool>? pasteTextIntoCommandLine = null,
        Func<string, object?, bool>? execute = null,
        Func<ConsoleModifiers, bool>? setFunctionKeyLayer = null)
    {
        var panel = new FilePanelState { CurrentDirectory = @"C:\work" };
        leftPanel ??= () => panel;
        rightPanel ??= () => panel;
        return new KeyboardInputRouter(new KeyboardInputContext
        {
            PanelController = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService())),
            CommandLine = commandLine,
            SetActiveSide = _ => { },
            LeftPanel = leftPanel,
            RightPanel = rightPanel,
            PanelOptions = () => new AppSettings.PanelOptionsSettings(),
            QuickView = () => false,
            SetQuickView = _ => { },
            SetRunning = _ => { },
            SetFunctionKeyLayer = setFunctionKeyLayer ?? (_ => false),
            ExecuteRegisteredCommand = execute ?? ((_, _) => false),
            ToggleSelectAllPanelItems = toggleSelectAllPanelItems ?? (_ => { }),
            CopyCommandLineSelection = () => false,
            PasteTextIntoCommandLine = pasteTextIntoCommandLine ?? (_ => false),
            OnCommandLineTextEdited = () => { },
            CloseSearchResultsPanel = (_, _) => { },
            ExecuteCommand = _ => { },
            BrowseCommandHistory = (_, _) => false,
            HideCommandCompletion = _ => { },
            ResetCommandHistoryNavigation = () => { },
            TryGoUp = (_, _) => { },
            OpenPanelItem = openPanelItem ?? ((_, _, _) => { }),
        });
    }

    private static ApplicationUiFrame Frame(CommandLineState commandLine) =>
        new(
            new ConsoleViewport(0, 0, 120, 25),
            ApplicationWorkspaceMode.Panels,
            KeyboardFrame(PanelSide.Left, commandLine.HasText, commandLine.HasSelection),
            new ApplicationCommandLineFrame(
                new Rect(0, 24, 120, 1),
                PromptLength: 8,
                DisplayOffset: 0,
                TextLength: commandLine.Text.Length,
                Cursor: new UiCursorPlacement(8, 24)),
            null,
            null,
            null,
            null);

    private static ApplicationKeyboardFrame KeyboardFrame(
        PanelSide activeSide,
        bool commandLineHasText = false,
        bool commandLineHasSelection = false,
        ApplicationPanelKeyboardFrame? leftPanel = null,
        ApplicationPanelKeyboardFrame? rightPanel = null) =>
        new(
            activeSide,
            commandLineHasText,
            commandLineHasSelection,
            leftPanel ?? PanelKeyboard(@"C:\left"),
            rightPanel ?? PanelKeyboard(@"C:\right"));

    private static ApplicationPanelKeyboardFrame PanelKeyboard(
        string currentDirectory,
        int? currentItemIndex = null,
        PanelLocation? currentItemLocation = null,
        string? currentItemName = null) =>
        new(
            PanelLocation.Local(currentDirectory),
            false,
            currentItemIndex,
            currentItemLocation,
            currentItemName);
}
