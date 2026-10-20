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
    [InlineData('[', ConsoleKey.Oem4, @"C:\committed-left")]
    [InlineData(']', ConsoleKey.Oem6, @"C:\committed-right")]
    public void WorkspaceKeyboard_CtrlBracketInsertsCommittedPanelDirectory(
        char keyChar,
        ConsoleKey key,
        string expectedDirectory)
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
        Assert.Equal(expectedDirectory + System.IO.Path.DirectorySeparatorChar, commandLine.Text);
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


}
