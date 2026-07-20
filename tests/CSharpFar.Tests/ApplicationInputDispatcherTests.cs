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
    public void DirectoryShortcutTarget_ExecutesOnlyShortcutHandlerWithCommittedPath()
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
            DirectoryShortcutBar = new ApplicationDirectoryShortcutBarFrame(
                [new ApplicationDirectoryShortcutHit(new Rect(1, 22, 9, 1), 1, @"C:\Rendered")]),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(2, 22, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            ApplicationTargetIds.DirectoryShortcutBar,
            UiInputRouteKind.HitTarget));

        Assert.True(request.ShouldRender);
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

    [Fact]
    public void WorkspaceKeyboard_UsesCommittedActiveSideAfterLiveActiveSideChanges()
    {
        var left = PanelStateWithItems();
        var right = PanelStateWithItems();
        PanelSide? openedSide = null;
        FilePanelState? openedState = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(
                commandLine,
                leftPanel: () => left,
                rightPanel: () => right,
                openPanelItem: (state, side, _) =>
                {
                    openedState = state;
                    openedSide = side;
                }),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(
                PanelSide.Left,
                leftPanel: PanelKeyboard(
                    @"C:\work",
                    0,
                    PanelLocation.Local(@"C:\work\a.txt"),
                    "a.txt")),
            LeftPanel = new ApplicationPanelFrame(
                PanelSide.Left,
                new Rect(0, 0, 40, 10),
                8,
                [],
                null,
                null),
            RightPanel = new ApplicationPanelFrame(PanelSide.Right, new Rect(40, 0, 40, 10), 8, [], null, null),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Same(left, openedState);
        Assert.Equal(PanelSide.Left, openedSide);
    }

    [Fact]
    public void WorkspaceKeyboard_EnterWithStaleCommittedItemIsHandledAndDoesNotOpenDifferentItem()
    {
        var left = PanelStateWithItems();
        FilePanelItem? opened = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(
                commandLine,
                leftPanel: () => left,
                openPanelItem: (_, _, item) => opened = item),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Left),
            LeftPanel = new ApplicationPanelFrame(
                PanelSide.Left,
                new Rect(0, 0, 40, 10),
                8,
                [],
                null,
                null),
        };
        left.Items[0] = left.Items[1];

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Null(opened);
    }

    [Fact]
    public void WorkspaceKeyboard_EnterWithChangedProviderDoesNotOpenSamePathItem()
    {
        var left = PanelStateWithItems();
        left.CurrentLocation = new PanelLocation(new PanelSourceId("provider-b"), @"C:\work");
        left.Items[0] = new FilePanelItem
        {
            Name = "a.txt",
            FullPath = @"C:\work\a.txt",
            SourceId = new PanelSourceId("provider-b"),
            IsDirectory = false,
        };
        FilePanelItem? opened = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, leftPanel: () => left, openPanelItem: (_, _, item) => opened = item),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(
                PanelSide.Left,
                leftPanel: PanelKeyboard(
                    @"C:\work",
                    0,
                    new PanelLocation(new PanelSourceId("provider-a"), @"C:\work\a.txt"),
                    "a.txt")),
            LeftPanel = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 8, [], null, null),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Null(opened);
    }

    [Fact]
    public void WorkspaceKeyboard_CtrlFWithStaleCommittedItemDoesNotInsertDifferentPath()
    {
        var left = PanelStateWithItems();
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, leftPanel: () => left),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Left),
            LeftPanel = new ApplicationPanelFrame(
                PanelSide.Left,
                new Rect(0, 0, 40, 10),
                8,
                [],
                null,
                null),
        };
        left.Items[0] = left.Items[1];

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\u0006', ConsoleKey.F, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(string.Empty, commandLine.Text);
    }

    [Fact]
    public void WorkspaceKeyboard_CtrlAUsesCommittedCommandLineState()
    {
        int panelToggles = 0;
        var commandLine = new CommandLineState();
        commandLine.SetText("live text");
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, toggleSelectAllPanelItems: _ => panelToggles++),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Left, commandLineHasText: false),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\u0001', ConsoleKey.A, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(1, panelToggles);
        Assert.False(commandLine.HasSelection);
    }

    [Fact]
    public void WorkspaceKeyboard_CtrlADoesNotTogglePanelWhenCommittedCommandLineHasText()
    {
        int panelToggles = 0;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, toggleSelectAllPanelItems: _ => panelToggles++),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Left, commandLineHasText: true),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\u0001', ConsoleKey.A, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(0, panelToggles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceKeyboard_CtrlVPassesCommittedWorkspaceMode(bool hiddenCommandLine)
    {
        var mode = hiddenCommandLine
            ? ApplicationWorkspaceMode.HiddenCommandLine
            : ApplicationWorkspaceMode.Panels;
        ApplicationWorkspaceMode? pastedMode = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, pasteTextIntoCommandLine: committedMode =>
            {
                pastedMode = committedMode;
                return true;
            }),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Mode = mode,
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\u0016', ConsoleKey.V, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(mode, pastedMode);
    }

    [Theory]
    [InlineData('\r', ConsoleKey.Enter, "a.txt")]
    [InlineData('\u0006', ConsoleKey.F, @"C:\work\a.txt")]
    public void WorkspaceKeyboard_HiddenCommandLinePanelItemShortcutsUseKeyboardSnapshot(
        char keyChar,
        ConsoleKey key,
        string expectedText)
    {
        var left = PanelStateWithItems();
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, leftPanel: () => left),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Mode = ApplicationWorkspaceMode.HiddenCommandLine,
            Keyboard = KeyboardFrame(
                PanelSide.Left,
                leftPanel: PanelKeyboard(
                    @"C:\work",
                    0,
                    PanelLocation.Local(@"C:\work\a.txt"),
                    "a.txt")),
            LeftPanel = null,
            RightPanel = null,
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo(keyChar, key, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(expectedText, commandLine.Text);
    }

    [Theory]
    [InlineData('[', ConsoleKey.Oem4, @"C:\committed-left\")]
    [InlineData(']', ConsoleKey.Oem6, @"C:\committed-right\")]
    public void WorkspaceKeyboard_CtrlBracketInsertsCommittedPanelDirectory(
        char keyChar,
        ConsoleKey key,
        string expectedText)
    {
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(
                commandLine,
                leftPanel: () => new FilePanelState { CurrentDirectory = @"C:\live-left" },
                rightPanel: () => new FilePanelState { CurrentDirectory = @"C:\live-right" }),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(
                PanelSide.Left,
                leftPanel: new ApplicationPanelKeyboardFrame(
                    PanelLocation.Local(@"C:\committed-left"),
                    false,
                    null,
                    null,
                    null),
                rightPanel: new ApplicationPanelKeyboardFrame(
                    PanelLocation.Local(@"C:\committed-right"),
                    false,
                    null,
                    null,
                    null)),
            LeftPanel = new ApplicationPanelFrame(
                PanelSide.Left,
                new Rect(0, 0, 40, 10),
                8,
                [],
                null,
                null),
            RightPanel = new ApplicationPanelFrame(
                PanelSide.Right,
                new Rect(40, 0, 40, 10),
                8,
                [],
                null,
                null),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo(keyChar, key, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.Equal(expectedText, commandLine.Text);
    }

    [Fact]
    public void WorkspaceKeyboard_DirectoryShortcutUsesCommittedPathAndSide()
    {
        (string CommandId, object? Args)? executed = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(
                commandLine,
                rightPanel: () => new FilePanelState { CurrentDirectory = @"C:\live" },
                execute: (commandId, args) =>
                {
                    executed = (commandId, args);
                    return true;
                }),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Right),
            DirectoryShortcutBar = new ApplicationDirectoryShortcutBarFrame(
                [new ApplicationDirectoryShortcutHit(new Rect(0, 22, 8, 1), 2, @"C:\committed")]),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.D2, false, false, true)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.NotNull(executed);
        var args = Assert.IsType<NavigateToCommittedDirectoryShortcutArgs>(executed.Value.Args);
        Assert.Equal(2, args.Number);
        Assert.Equal(@"C:\committed", args.Path);
        Assert.Equal(PanelSide.Right, args.Side);
    }

    [Fact]
    public void WorkspaceKeyboard_FunctionKeyUsesCommittedCommandAndSide()
    {
        (string CommandId, object? Args)? executed = null;
        var commandLine = new CommandLineState();
        var dispatcher = new ApplicationInputDispatcher(
            KeyboardRouter(commandLine, execute: (commandId, args) =>
            {
                executed = (commandId, args);
                return true;
            }),
            new ApplicationCommandLineInputHandler(Context(commandLine)),
            new ApplicationPanelInputHandler(Context(commandLine)),
            new ApplicationPanelScrollbarInputHandler(Context(commandLine)),
            new ApplicationFunctionKeyBarInputHandler(Context(commandLine)),
            new ApplicationDirectoryShortcutBarInputHandler(Context(commandLine)));
        var frame = Frame(commandLine) with
        {
            Keyboard = KeyboardFrame(PanelSide.Right),
            FunctionKeyBar = new ApplicationFunctionKeyBarFrame(
                [new ApplicationFunctionKeyHit(
                    new Rect(0, 24, 8, 1),
                    FunctionKeyCommandIds.Search,
                    FunctionKeyLayer.Alt,
                    ConsoleKey.F7)]),
        };

        var request = dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.F7, false, true, false)),
            frame,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));

        Assert.True(request.ShouldRender);
        Assert.NotNull(executed);
        Assert.Equal(FunctionKeyCommandIds.Search, executed.Value.CommandId);
        var args = Assert.IsType<ApplicationPanelCommandInvocation>(executed.Value.Args);
        Assert.Equal(PanelSide.Right, args.Side);
    }

    [Fact]
    public void KeyboardRouting_RemovesCompatibilityAndLiveRoutingApis()
    {
        Assert.Null(typeof(KeyboardInputRouter).GetMethod("HandleModifier"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("IsPanelsMode"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("ActiveState"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("VisibleRows"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("VisibleRowsForSide"));
    }

    [Fact]
    public void UnknownOrLayerMouseTarget_DoesNotChangeApplicationState()
    {
        var state = PanelStateWithItems();
        var commandLine = new CommandLineState();
        int executions = 0;
        var dispatcher = Dispatcher(Context(commandLine, panelState: state, execute: (_, _) => { executions++; return true; }));
        var frame = Frame(commandLine) with
        {
            LeftPanel = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 8,
                [new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"))], null, null),
        };

        Assert.False(dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, new UiTargetId("application.unknown"), UiInputRouteKind.HitTarget)).ShouldRender);
        Assert.False(dispatcher.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame, ApplicationTargetIds.LeftPanel, UiInputRouteKind.Layer)).ShouldRender);
        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void PanelLeftDown_UsesCommittedItemHitSnapshotAfterSourceListMutates()
    {
        var state = PanelStateWithItems();
        var context = Context(new CommandLineState(), panelState: state);
        var handler = new ApplicationPanelInputHandler(context);
        var hits = new List<ApplicationPanelItemHit>
        {
            new(new Rect(1, 1, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt")),
        };
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            hits,
            null,
            null);
        hits[0] = new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"));

        var result = handler.Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(new PanelItemClick(PanelSide.Left, 0, PanelLocation.Local(@"C:\work\a.txt")), context.Mouse.LastLeftPanelItemClick);
    }

    [Fact]
    public void PanelDoubleClick_DoesNotOpenDifferentItemAfterReorder()
    {
        var state = PanelStateWithItems();
        (PanelSide Side, FilePanelItem Item)? opened = null;
        var context = Context(
            new CommandLineState(),
            panelState: state,
            openPanelItem: (_, side, item) => opened = (side, item));
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(PanelSide.Left, 0, PanelLocation.Local(@"C:\work\a.txt"));
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            [new ApplicationPanelItemHit(new Rect(1, 1, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt"))],
            null,
            null);
        (state.Items[0], state.Items[1]) = (state.Items[1], state.Items[0]);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Null(opened);
    }

    [Fact]
    public void PanelDoubleClick_DoesNotOpenSamePathFromDifferentProvider()
    {
        var state = PanelStateWithItems();
        state.Items[0] = new FilePanelItem
        {
            Name = "a.txt",
            FullPath = @"C:\work\a.txt",
            SourceId = new PanelSourceId("provider-b"),
            IsDirectory = false,
        };
        (PanelSide Side, FilePanelItem Item)? opened = null;
        var context = Context(new CommandLineState(), panelState: state,
            openPanelItem: (_, side, item) => opened = (side, item));
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(
            PanelSide.Left,
            0,
            new PanelLocation(new PanelSourceId("provider-a"), @"C:\work\a.txt"));
        var frame = new ApplicationPanelFrame(
            PanelSide.Left,
            new Rect(0, 0, 40, 10),
            8,
            [new ApplicationPanelItemHit(
                new Rect(1, 1, 10, 1),
                0,
                new PanelLocation(new PanelSourceId("provider-b"), @"C:\work\a.txt"))],
            null,
            null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None),
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Null(opened);
    }

    [Theory]
    [InlineData(ScrollBarHitPart.DecreaseButton)]
    [InlineData(ScrollBarHitPart.IncreaseButton)]
    [InlineData(ScrollBarHitPart.TrackBeforeThumb)]
    [InlineData(ScrollBarHitPart.TrackAfterThumb)]
    public void PanelScrollbarClick_UsesCommittedFrameWithoutCreatingDrag(ScrollBarHitPart part)
    {
        var state = PanelStateWithItems(100);
        state.ScrollOffset = 30;
        PanelSide? activeSide = null;
        var context = Context(new CommandLineState(), panelState: state, setActiveSide: side => activeSide = side);
        context.Mouse.LastLeftPanelItemClick = new PanelItemClick(PanelSide.Right, 4, PanelLocation.Local("old"));
        var frame = new ApplicationScrollBarFrame(new Rect(10, 10, 1, 20), 100, 10, 30);
        var hit = ScrollBarInteraction.CalculateThumb(frame.Bounds, frame.ToScrollState());
        int y = part switch
        {
            ScrollBarHitPart.DecreaseButton => frame.Bounds.Y,
            ScrollBarHitPart.IncreaseButton => frame.Bounds.Bottom - 1,
            ScrollBarHitPart.TrackBeforeThumb => hit.ThumbY - 1,
            ScrollBarHitPart.TrackAfterThumb => hit.ThumbY + hit.ThumbHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

        var result = new ApplicationPanelScrollbarInputHandler(context).Handle(
            new MouseConsoleInputEvent(frame.Bounds.X, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            PanelSide.Left,
            frame,
            UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.True(result.ShouldRender);
        Assert.Equal(PanelSide.Left, activeSide);
        Assert.Equal(ScrollBarInteraction.ApplyClick(frame.ToScrollState(), part), state.ScrollOffset);
        Assert.Null(context.Mouse.LastLeftPanelItemClick);
        Assert.Null(context.Ui.PanelScrollbarDrag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelRightClick_UsesCommittedHitAndSelectionSetting(bool selectsFiles)
    {
        var state = PanelStateWithItems();
        PanelSide? activeSide = null;
        var options = new AppSettings.PanelOptionsSettings { RightClickSelectsFiles = selectsFiles };
        var context = Context(new CommandLineState(), panelState: state, options: options, setActiveSide: side => activeSide = side);
        var frame = new ApplicationPanelFrame(PanelSide.Right, new Rect(40, 0, 40, 10), 1,
            [new ApplicationPanelItemHit(new Rect(41, 2, 10, 1), 1, PanelLocation.Local(@"C:\work\b.txt"))], null, null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(41, 2, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(PanelSide.Right, activeSide);
        Assert.Equal(1, state.CursorIndex);
        Assert.Equal(selectsFiles, state.SelectedPaths.Contains(@"C:\work\b.txt"));
    }

    [Fact]
    public void PanelRightClick_StaleIdentityDoesNotMoveCursorOrSelection()
    {
        var state = PanelStateWithItems();
        state.CursorIndex = 0;
        var context = Context(new CommandLineState(), panelState: state);
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 8,
            [new ApplicationPanelItemHit(new Rect(1, 2, 10, 1), 0, PanelLocation.Local(@"C:\work\a.txt"))], null, null);
        state.Items[0] = state.Items[1];

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 2, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(0, state.CursorIndex);
        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void PanelWheel_UsesCommittedVisibleRows()
    {
        var state = PanelStateWithItems(20);
        state.ScrollOffset = 14;
        state.CursorIndex = 14;
        var context = Context(new CommandLineState(), panelState: state);
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 3, [], null, null);

        var result = new ApplicationPanelInputHandler(context).Handle(
            new MouseConsoleInputEvent(1, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget);

        Assert.True(result.Handled);
        Assert.Equal(17, state.ScrollOffset);
    }

    [Fact]
    public void PanelRetry_UsesCommittedBoundsAndVisibleRows()
    {
        var state = PanelStateWithItems();
        int refreshCalls = 0;
        int visibleRows = 0;
        var context = Context(new CommandLineState(), panelState: state, safeRefresh: (_, rows) => { refreshCalls++; visibleRows = rows; });
        var frame = new ApplicationPanelFrame(PanelSide.Left, new Rect(0, 0, 40, 10), 7, [], new Rect(2, 3, 5, 1), null);
        var handler = new ApplicationPanelInputHandler(context);

        Assert.True(handler.Handle(new MouseConsoleInputEvent(3, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget).Handled);
        Assert.True(handler.Handle(new MouseConsoleInputEvent(20, 3, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget).Handled);
        Assert.Equal(1, refreshCalls);
        Assert.Equal(7, visibleRows);
    }

    [Fact]
    public void BriefRenderer_MetadataExcludesSeparatorAndRoutesSecondColumnItem()
    {
        var state = PanelStateWithItems(20);
        var bounds = new Rect(0, 0, 40, 10);
        var renderer = new BriefTwoColumnsPanelRenderer(
            new ScreenRenderer(new FakeConsoleDriver(80, 25)),
            PaletteRegistry.Default);
        ApplicationPanelFrame frame = renderer.Render(bounds, state, isActive: true, PanelSide.Left);
        Assert.NotEmpty(frame.VisibleItems);

        int separatorX = Enumerable.Range(bounds.X + 1, bounds.Width - 2)
            .Single(x => frame.VisibleItems.All(hit => !hit.Bounds.Contains(x, frame.VisibleItems[0].Bounds.Y)));
        ApplicationPanelItemHit secondColumnItem = frame.VisibleItems
            .First(hit => hit.Bounds.X > separatorX);
        var handler = new ApplicationPanelInputHandler(Context(new CommandLineState(), panelState: state));

        handler.Handle(new MouseConsoleInputEvent(separatorX, secondColumnItem.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget);
        Assert.Equal(0, state.CursorIndex);
        handler.Handle(new MouseConsoleInputEvent(secondColumnItem.Bounds.X, secondColumnItem.Bounds.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None), frame, UiInputRouteKind.HitTarget);
        Assert.Equal(secondColumnItem.ItemIndex, state.CursorIndex);
    }

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
        Func<string, object?, bool>? execute = null)
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
            SetFunctionKeyLayer = _ => false,
            ExecuteRegisteredCommand = execute ?? ((_, _) => false),
            ToggleSelectAllPanelItems = toggleSelectAllPanelItems ?? (_ => { }),
            CopyCommandLineSelection = () => false,
            PasteTextIntoCommandLine = pasteTextIntoCommandLine ?? (_ => false),
            OnVisibleCommandLineTextEdited = () => { },
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
