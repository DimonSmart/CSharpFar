using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec008MenuControllerTests
{
    [Fact]
    public void F9_OpensLeftDropdown_WhenActivePanelIsLeft()
    {
        var state = new MenuState();
        var controller = CreateController(state);

        Assert.True(controller.HandleKey(Key(ConsoleKey.F9), Definition(), PanelSide.Left));

        Assert.Equal(MenuOpenState.DropdownOpen, state.OpenState);
        Assert.Equal(0, state.ActiveTopMenuIndex);
        Assert.Equal(0, state.ActiveDropdownItemIndex);
    }

    [Fact]
    public void F9_OpensRightDropdown_WhenActivePanelIsRight()
    {
        var state = new MenuState();
        var controller = CreateController(state);

        controller.HandleKey(Key(ConsoleKey.F9), Definition(), PanelSide.Right);

        Assert.Equal(1, state.ActiveTopMenuIndex);
    }

    [Fact]
    public void Esc_ClosesOpenMenu()
    {
        var state = OpenState();
        var controller = CreateController(state);

        controller.HandleKey(Key(ConsoleKey.Escape), Definition(), PanelSide.Left);

        Assert.Equal(MenuOpenState.Closed, state.OpenState);
    }

    [Fact]
    public void UpDown_SkipSeparatorsAndDisabledItems()
    {
        var state = OpenState();
        var controller = CreateController(state);

        controller.HandleKey(Key(ConsoleKey.DownArrow), Definition(), PanelSide.Left);

        Assert.Equal(3, state.ActiveDropdownItemIndex);

        controller.HandleKey(Key(ConsoleKey.UpArrow), Definition(), PanelSide.Left);

        Assert.Equal(0, state.ActiveDropdownItemIndex);
    }

    [Fact]
    public void Enter_ExecutesSelectedCommandAndClosesMenu()
    {
        var state = OpenState();
        var executed = new List<string>();
        var controller = CreateController(state, request => executed.Add(request.CommandId));

        controller.HandleKey(Key(ConsoleKey.Enter), Definition(), PanelSide.Left);

        Assert.Equal(["left.full"], executed);
        Assert.Equal(MenuOpenState.Closed, state.OpenState);
    }

    [Fact]
    public void Enter_ClosesMenuBeforeExecutingSelectedCommand()
    {
        var state = OpenState();
        MenuOpenState? stateDuringExecution = null;
        var controller = CreateController(state, _ => stateDuringExecution = state.OpenState);

        controller.HandleKey(Key(ConsoleKey.Enter), Definition(), PanelSide.Left);

        Assert.Equal(MenuOpenState.Closed, stateDuringExecution);
    }

    [Fact]
    public void Enter_OnDisabledItem_DoesNotExecute()
    {
        var state = OpenState();
        state.ActiveDropdownItemIndex = 2;
        int executed = 0;
        var controller = CreateController(state, _ => executed++);

        controller.HandleKey(Key(ConsoleKey.Enter), Definition(), PanelSide.Left);

        Assert.Equal(0, executed);
        Assert.Equal(MenuOpenState.DropdownOpen, state.OpenState);
    }

    [Fact]
    public void Hotkey_ExecutesFirstMatchingEnabledDropdownItem()
    {
        var state = OpenState();
        var executed = new List<string>();
        var controller = CreateController(state, request => executed.Add(request.CommandId));

        controller.HandleKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false), Definition(), PanelSide.Left);

        Assert.Equal(["left.refresh"], executed);
        Assert.Equal(MenuOpenState.Closed, state.OpenState);
    }

    [Fact]
    public void Hotkey_ActivatesMatchingTopMenuItem()
    {
        var state = OpenState();
        var controller = CreateController(state);

        controller.HandleKey(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false), Definition(), PanelSide.Left);

        Assert.Equal(2, state.ActiveTopMenuIndex);
        Assert.Equal(MenuOpenState.DropdownOpen, state.OpenState);
    }

    [Fact]
    public void Tab_SwitchesLeftAndRightMenus()
    {
        var definition = Definition();
        var state = OpenState();
        var controller = CreateController(state);

        controller.HandleKey(Key(ConsoleKey.Tab), definition, PanelSide.Left);

        Assert.Equal(1, state.ActiveTopMenuIndex);

        controller.HandleKey(Key(ConsoleKey.Tab), definition, PanelSide.Left);

        Assert.Equal(0, state.ActiveTopMenuIndex);
    }

    [Fact]
    public void Tab_FromOptions_SwitchesToPassivePanelMenu()
    {
        var state = OpenState(topIndex: 2);
        var controller = CreateController(state);

        controller.HandleKey(Key(ConsoleKey.Tab), Definition(), PanelSide.Left);

        Assert.Equal(1, state.ActiveTopMenuIndex);
    }

    [Fact]
    public void MouseDown_OnTopItem_OpensThatDropdown()
    {
        var definition = Definition();
        var state = new MenuState();
        var controller = CreateController(state);
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);

        controller.HandleMouse(
            new MouseConsoleInputEvent(layout.TopItemBounds[1].X + 1, 0, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            definition,
            layout,
            PanelSide.Left);

        Assert.Equal(MenuOpenState.DropdownOpen, state.OpenState);
        Assert.Equal(1, state.ActiveTopMenuIndex);
    }

    [Fact]
    public void MouseDown_OnDropdownCommand_ExecutesCommand()
    {
        var definition = Definition();
        var state = OpenState();
        var executed = new List<string>();
        var controller = CreateController(state, request => executed.Add(request.CommandId));
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var dropdown = layout.DropdownBounds!.Value;

        controller.HandleMouse(
            new MouseConsoleInputEvent(dropdown.X + 1, dropdown.Y + 1, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            definition,
            layout,
            PanelSide.Left);

        Assert.Equal(["left.full"], executed);
        Assert.Equal(MenuOpenState.Closed, state.OpenState);
    }

    [Fact]
    public void MouseDown_OutsideOpenMenu_ClosesMenu()
    {
        var definition = Definition();
        var state = OpenState();
        var controller = CreateController(state);
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);

        controller.HandleMouse(
            new MouseConsoleInputEvent(79, 24, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None),
            definition,
            layout,
            PanelSide.Left);

        Assert.Equal(MenuOpenState.Closed, state.OpenState);
    }

    private static TopMenuController CreateController(MenuState state, Action<MenuCommandRequest>? execute = null) =>
        new(state, request =>
        {
            execute?.Invoke(request);
            return new MenuCommandResult { Success = true };
        });

    private static MenuState OpenState(int topIndex = 0) =>
        new()
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = topIndex,
            ActiveDropdownItemIndex = 0,
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static MenuBarDefinition Definition() =>
        new()
        {
            Items =
            [
                Top("Left", "left"),
                Top("Right", "right"),
                new TopMenuItemDefinition
                {
                    Id = "Options",
                    Text = "Options",
                    HotChar = 'O',
                    Children =
                    [
                        new MenuItemDefinition
                        {
                            Id = "options.panel",
                            Text = "Panel settings",
                            HotChar = 'P',
                            CommandId = "options.panel",
                        },
                    ],
                },
            ],
        };

    private static TopMenuItemDefinition Top(string text, string commandPrefix) =>
        new()
        {
            Id = text,
            Text = text,
            HotChar = text[0],
            Children =
            [
                new MenuItemDefinition
                {
                    Id = $"{commandPrefix}.full",
                    Text = "Full mode",
                    HotChar = 'F',
                    CommandId = $"{commandPrefix}.full",
                },
                new MenuItemDefinition
                {
                    Id = $"{commandPrefix}.sep",
                    Text = string.Empty,
                    Kind = MenuItemKind.Separator,
                    IsEnabled = false,
                },
                new MenuItemDefinition
                {
                    Id = $"{commandPrefix}.disabled",
                    Text = "Disabled",
                    HotChar = 'D',
                    CommandId = $"{commandPrefix}.disabled",
                    IsEnabled = false,
                },
                new MenuItemDefinition
                {
                    Id = $"{commandPrefix}.refresh",
                    Text = "Refresh",
                    HotChar = 'R',
                    CommandId = $"{commandPrefix}.refresh",
                },
            ],
        };
}

public sealed class Spec008MenuLayoutAndRenderingTests
{
    [Fact]
    public void Layout_TopItems_AreInFileCommandsLeftRightPluginsOptionsOrder()
    {
        var layout = new MenuLayoutService().CalculateLayout(
            new Rect(0, 0, 80, 25),
            ProviderMenu(),
            new MenuState());

        Assert.Equal(6, layout.TopItemBounds.Count);
        Assert.True(layout.TopItemBounds[0].X < layout.TopItemBounds[1].X);
        Assert.True(layout.TopItemBounds[1].X < layout.TopItemBounds[2].X);
        Assert.True(layout.TopItemBounds[2].X < layout.TopItemBounds[3].X);
        Assert.True(layout.TopItemBounds[3].X < layout.TopItemBounds[4].X);
        Assert.True(layout.TopItemBounds[4].X < layout.TopItemBounds[5].X);
    }

    [Fact]
    public void Layout_Dropdown_IsBelowActiveTopItemAndShiftsLeftAtScreenEdge()
    {
        var definition = ProviderMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 2,
        };

        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 24, 10), definition, state);

        Assert.NotNull(layout.DropdownBounds);
        Assert.Equal(1, layout.DropdownBounds!.Value.Y);
        Assert.True(layout.DropdownBounds.Value.Right <= 24);
    }

    [Fact]
    public void HitTest_DistinguishesTopDropdownBorderAndOutside()
    {
        var definition = ProviderMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var tester = new MenuHitTester();
        var dropdown = layout.DropdownBounds!.Value;

        Assert.Equal(MenuHitTestKind.TopMenuItem,
            tester.HitTest(1, 0, definition, state, layout).Kind);
        Assert.Equal(MenuHitTestKind.DropdownBorder,
            tester.HitTest(dropdown.X, dropdown.Y, definition, state, layout).Kind);
        Assert.Equal(MenuHitTestKind.DropdownItem,
            tester.HitTest(dropdown.X + 1, dropdown.Y + 1, definition, state, layout).Kind);
        Assert.Equal(MenuHitTestKind.Outside,
            tester.HitTest(79, 24, definition, state, layout).Kind);
    }

    [Fact]
    public void PopupRenderer_DrawsShadowClippedByScreen()
    {
        var driver = new FakeConsoleDriver(width: 5, height: 4);
        var screen = new ScreenRenderer(driver);
        var renderer = new PopupRenderer();
        var shadow = new CellStyle(ConsoleColor.Red, ConsoleColor.Yellow);

        renderer.RenderPopup(
            screen,
            new Rect(2, 1, 2, 2),
            new PopupRenderOptions
            {
                BorderStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Blue),
                BackgroundStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Blue),
                ShadowStyle = shadow,
                DrawBorder = false,
            },
            (_, _) => { });

        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(4, 2).Background);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(4, 3).Background);
    }

    [Fact]
    public void DropdownMenuRenderer_UsesPopupShadow()
    {
        var definition = ProviderMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        var screen = new ScreenRenderer(driver);
        var options = MenuOptions();
        var dropdown = layout.DropdownBounds!.Value;

        new DropdownMenuRenderer().Render(screen, definition, state, layout, options);

        Assert.Equal(options.ShadowStyle.Background, driver.GetCell(dropdown.Right, dropdown.Y + 1).Background);
    }

    [Fact]
    public void DropdownLayout_IncludesShortcutWidth()
    {
        var definition = ShortcutMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var withoutShortcut = new MenuLayoutService()
            .CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var withShortcut = ShortcutLayoutService()
            .CalculateLayout(new Rect(0, 0, 80, 25), definition, state);

        Assert.True(withShortcut.DropdownBounds!.Value.Width > withoutShortcut.DropdownBounds!.Value.Width);
    }

    [Fact]
    public void DropdownRenderer_RendersShortcutAtRightSide()
    {
        var definition = ShortcutMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var layoutService = ShortcutLayoutService();
        var layout = layoutService.CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        var screen = new ScreenRenderer(driver);

        new DropdownMenuRenderer(layoutService).Render(screen, definition, state, layout, MenuOptions());

        var dropdown = layout.DropdownBounds!.Value;
        string content = driver.GetRow(dropdown.Y + 1)
            .Substring(dropdown.X + 1, dropdown.Width - 2);
        Assert.EndsWith("Ctrl+O", content, StringComparison.Ordinal);
        Assert.Contains("Panels on/off", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DropdownRenderer_HighlightsHotCharInTextOnly()
    {
        var definition = ShortcutMenu();
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var layoutService = ShortcutLayoutService();
        var layout = layoutService.CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        var screen = new ScreenRenderer(driver);
        var options = MenuOptions();

        new DropdownMenuRenderer(layoutService).Render(screen, definition, state, layout, options);

        var dropdown = layout.DropdownBounds!.Value;
        int row = dropdown.Y + 1;
        int contentX = dropdown.X + 1;
        Assert.Equal('P', driver.GetCell(contentX + 4, row).Character);
        Assert.Equal(options.ActiveHighlightStyle.Foreground, driver.GetCell(contentX + 4, row).Foreground);

        int shortcutX = driver.GetRow(row).IndexOf("Ctrl+O", StringComparison.Ordinal);
        Assert.True(shortcutX >= 0);
        Assert.NotEqual(options.ActiveHighlightStyle.Foreground, driver.GetCell(shortcutX, row).Foreground);
    }

    [Fact]
    public void DropdownRenderer_NoShortcut_KeepsOldLayout()
    {
        var definition = new MenuBarDefinition
        {
            Items =
            [
                new TopMenuItemDefinition
                {
                    Id = "Commands",
                    Text = "Commands",
                    HotChar = 'C',
                    Children =
                    [
                        new MenuItemDefinition
                        {
                            Id = "Commands.swapPanels",
                            Text = "Swap panels",
                            HotChar = 'S',
                            CommandId = ApplicationCommandIds.SwapPanels,
                        },
                    ],
                },
            ],
        };
        var state = new MenuState
        {
            OpenState = MenuOpenState.DropdownOpen,
            ActiveTopMenuIndex = 0,
        };
        var layout = new MenuLayoutService().CalculateLayout(new Rect(0, 0, 80, 25), definition, state);
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        var screen = new ScreenRenderer(driver);

        new DropdownMenuRenderer().Render(screen, definition, state, layout, MenuOptions());

        var dropdown = layout.DropdownBounds!.Value;
        string content = driver.GetRow(dropdown.Y + 1)
            .Substring(dropdown.X + 1, dropdown.Width - 2);
        Assert.Equal("    Swap panels", content);
    }

    [Fact]
    public void DialogFrameRenderer_UsesPopupShadow()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 10);
        var screen = new ScreenRenderer(driver);
        var options = new PopupRenderOptions
        {
            BorderStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Blue),
            BackgroundStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Blue),
            ShadowStyle = new CellStyle(ConsoleColor.Red, ConsoleColor.Yellow),
        };

        new DialogFrameRenderer().RenderFrame(screen, new Rect(2, 1, 8, 4), "Title", false, options, (_, _) => { });

        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(10, 2).Background);
    }

    private static MenuRenderOptions MenuOptions() =>
        new()
        {
            MenuBarNormalStyle = new CellStyle(ConsoleColor.Black, ConsoleColor.DarkCyan),
            MenuBarActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            NormalStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkCyan),
            ActiveStyle = new CellStyle(ConsoleColor.White, ConsoleColor.Black),
            HighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.DarkCyan),
            ActiveHighlightStyle = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black),
            DisabledStyle = new CellStyle(ConsoleColor.DarkGray, ConsoleColor.DarkCyan),
            BorderStyle = new CellStyle(ConsoleColor.White, ConsoleColor.DarkCyan),
            ShadowStyle = new CellStyle(ConsoleColor.Red, ConsoleColor.Yellow),
        };

    private static MenuBarDefinition ProviderMenu()
    {
        string temp = Path.GetTempPath();
        var state = new FilePanelState { CurrentDirectory = temp };
        return new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = state,
            RightPanel = state,
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.BriefTwoColumns,
            Settings = new AppSettings(),
            CanSaveSettings = true,
        });
    }

    private static MenuLayoutService ShortcutLayoutService() =>
        new(new CommandShortcutTextProvider(
            new DefaultKeyboardShortcutBindingProvider().GetBindings(),
            new DefaultFunctionKeyBindingProvider().GetBindings()));

    private static MenuBarDefinition ShortcutMenu() =>
        new()
        {
            Items =
            [
                new TopMenuItemDefinition
                {
                    Id = "Commands",
                    Text = "Commands",
                    HotChar = 'C',
                    Children =
                    [
                        new MenuItemDefinition
                        {
                            Id = "Commands.togglePanels",
                            Text = "Panels on/off",
                            HotChar = 'P',
                            CommandId = ApplicationCommandIds.TogglePanels,
                        },
                    ],
                },
            ],
        };
}

public sealed class Spec008MenuProviderAndCommandTests : IDisposable
{
    private readonly string _tempDir;

    public Spec008MenuProviderAndCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec008_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Provider_BuildsFileCommandsLeftRightPluginsOptionsWithoutWideMode()
    {
        var menu = BuildProviderMenu(canSaveSettings: false);

        Assert.Equal(["File", "Commands", "Left", "Right", "Plugins", "Options"], menu.Items.Select(i => i.Text).ToArray());
        Assert.DoesNotContain(menu.Items[2].Children, item => item.Text.Contains("Wide", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(menu.Items[5].Children, item => item.CommandId == MenuCommandIds.SettingsSave);
    }

    [Fact]
    public void MenuItemDefinition_UsesHotChar_NotHotKey()
    {
        Assert.NotNull(typeof(MenuItemDefinition).GetProperty(nameof(MenuItemDefinition.HotChar)));
        Assert.Null(typeof(MenuItemDefinition).GetProperty("HotKey"));
        Assert.NotNull(typeof(TopMenuItemDefinition).GetProperty(nameof(TopMenuItemDefinition.HotChar)));
        Assert.Null(typeof(TopMenuItemDefinition).GetProperty("HotKey"));
    }

    [Fact]
    public void CommandsMenu_IsPresentAfterFileMenu()
    {
        var menu = BuildProviderMenu(canSaveSettings: false);

        Assert.Equal("File", menu.Items[0].Text);
        Assert.Equal("Commands", menu.Items[1].Text);
        Assert.Equal('C', menu.Items[1].HotChar);
    }

    [Fact]
    public void CommandsMenu_ContainsPanelsOnOff()
    {
        var commands = BuildProviderMenu(canSaveSettings: false).Items[1];

        var item = commands.Children.Single(item => item.Text == "Panels on/off");

        Assert.Equal("Commands.togglePanels", item.Id);
        Assert.Equal('P', item.HotChar);
    }

    [Fact]
    public void CommandsMenu_ContainsSwapPanels()
    {
        var commands = BuildProviderMenu(canSaveSettings: false).Items[1];

        var item = commands.Children.Single(item => item.Text == "Swap panels");

        Assert.Equal("Commands.swapPanels", item.Id);
        Assert.Equal(ApplicationCommandIds.SwapPanels, item.CommandId);
        Assert.Equal('S', item.HotChar);
    }

    [Fact]
    public void PanelsOnOff_MenuItem_UsesTogglePanelsCommandId()
    {
        var commands = BuildProviderMenu(canSaveSettings: false).Items[1];

        var item = commands.Children.Single(item => item.Text == "Panels on/off");

        Assert.Equal(ApplicationCommandIds.TogglePanels, item.CommandId);
    }

    [Fact]
    public void PanelsOnOff_DisplaysCtrlO_FromShortcutRegistry()
    {
        var provider = ShortcutTextProvider();

        Assert.Equal("Ctrl+O", provider.GetPrimaryShortcutText(ApplicationCommandIds.TogglePanels));
    }

    [Fact]
    public void CommandShortcutTextProvider_ReturnsFunctionKeyShortcut()
    {
        var provider = ShortcutTextProvider();

        Assert.Equal("Alt+F7", provider.GetPrimaryShortcutText(FunctionKeyCommandIds.Search));
        Assert.Equal("F3", provider.GetPrimaryShortcutText(FunctionKeyCommandIds.View));
        Assert.Equal("Ctrl+F1", provider.GetPrimaryShortcutText(FunctionKeyCommandIds.ToggleLeftPanel));
    }

    [Fact]
    public void CommandShortcutTextProvider_ReturnsKeyboardShortcut()
    {
        var provider = ShortcutTextProvider();

        Assert.Equal("Ctrl+O", provider.GetPrimaryShortcutText(ApplicationCommandIds.TogglePanels));
    }

    [Fact]
    public void Provider_FileMenuContainsViewerEditorAndAttributesCommands()
    {
        var menu = BuildProviderMenu(canSaveSettings: false);
        var file = menu.Items[0];

        Assert.Equal("File", file.Text);
        Assert.Equal(
            ["View", "Edit", "Attributes"],
            file.Children.Select(item => item.Text).ToArray());
        Assert.Equal(
            [FunctionKeyCommandIds.View, FunctionKeyCommandIds.Edit, FunctionKeyCommandIds.Attributes],
            file.Children.Select(item => item.CommandId!).ToArray());
    }

    [Fact]
    public void Provider_UsesExpectedPanelCommandArgsAndLabels()
    {
        var menu = BuildProviderMenu(canSaveSettings: true);
        var left = menu.Items[2].Children;
        var right = menu.Items[3].Children;

        var leftBrief = left.Single(i => i.Text == "Brief mode");
        var rightFull = right.Single(i => i.Text == "Full mode");
        var lastWrite = left.Single(i => i.Text == "Sort by last write time");

        Assert.Equal(PanelSide.Left, ((SetPanelViewModeArgs)leftBrief.CommandArgs!).PanelSide);
        Assert.Equal(PanelViewMode.BriefTwoColumns, ((SetPanelViewModeArgs)leftBrief.CommandArgs!).ViewMode);
        Assert.Equal(PanelSide.Right, ((SetPanelViewModeArgs)rightFull.CommandArgs!).PanelSide);
        Assert.Equal(SortMode.LastWriteTime, ((SetPanelSortModeArgs)lastWrite.CommandArgs!).SortMode);
        Assert.Contains(menu.Items[4].Children, item =>
            item.CommandId == MenuCommandIds.ModuleOpen &&
            item.CommandArgs is ModuleOpenCommandArgs { ActionId: var actionId } &&
            actionId == SftpModuleIds.MenuActionId);
        Assert.Contains(menu.Items[4].Children, item =>
            item.CommandId == MenuCommandIds.ModuleOpen &&
            item.CommandArgs is ModuleOpenCommandArgs { ActionId: var actionId } &&
            actionId == FtpModuleIds.MenuActionId);
        Assert.Contains(menu.Items[5].Children, item => item.CommandId == MenuCommandIds.SettingsSave);
        Assert.Contains(menu.Items[5].Children, item =>
            item.Id == "Options.diagnostics" &&
            item.CommandId == MenuCommandIds.DiagnosticsPrintTerminalInfo);
    }

    [Fact]
    public void Provider_DisablesRefreshWhenTargetDirectoryDoesNotExist()
    {
        var missing = new FilePanelState { CurrentDirectory = Path.Combine(_tempDir, "missing") };
        var existing = new FilePanelState { CurrentDirectory = _tempDir };

        var menu = new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = missing,
            RightPanel = existing,
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.Full,
            Settings = new AppSettings(),
            CanSaveSettings = true,
        });

        Assert.False(menu.Items[2].Children.Single(i => i.Text == "Refresh").IsEnabled);
        Assert.True(menu.Items[3].Children.Single(i => i.Text == "Refresh").IsEnabled);
    }

    [Fact]
    public void Application_MenuCommand_TogglesHighlightFilesAndSaves()
    {
        int saveCount = 0;
        var settings = new AppSettings();
        var app = CreateApp(settings, () => saveCount++);

        var result = Execute(app, new MenuCommandRequest
        {
            CommandId = MenuCommandIds.SettingsToggleHighlightFiles,
        });

        Assert.True(result.Success);
        Assert.False(settings.Panels.FileHighlighting.Enabled);
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void Application_MenuCommand_SetRightBriefModeUpdatesSettings()
    {
        int saveCount = 0;
        var settings = new AppSettings();
        var app = CreateApp(settings, () => saveCount++);

        var result = Execute(app, new MenuCommandRequest
        {
            CommandId = MenuCommandIds.PanelSetViewMode,
            Args = new SetPanelViewModeArgs
            {
                PanelSide = PanelSide.Right,
                ViewMode = PanelViewMode.BriefTwoColumns,
            },
        });

        Assert.True(result.Success);
        Assert.Equal("BriefTwoColumns", settings.Panels.RightViewMode);
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void SwapPanels_SwapsLeftAndRightPanelStates()
    {
        var app = CreateApp(new AppSettings(), saveSettings: null);
        var leftBefore = app.Session.Panels.Left;
        var rightBefore = app.Session.Panels.Right;

        var result = Execute(app, new MenuCommandRequest { CommandId = ApplicationCommandIds.SwapPanels });

        Assert.True(result.Success);
        Assert.Same(rightBefore, app.Session.Panels.Left);
        Assert.Same(leftBefore, app.Session.Panels.Right);
    }

    [Fact]
    public void SwapPanels_KeepsActiveSide()
    {
        var app = CreateApp(new AppSettings(), saveSettings: null);
        app.Session.Panels.ActiveSide = PanelSide.Left;

        Execute(app, new MenuCommandRequest { CommandId = ApplicationCommandIds.SwapPanels });

        Assert.Equal(PanelSide.Left, app.Session.Panels.ActiveSide);
    }

    [Fact]
    public void SwapPanels_SwapsLeftAndRightViewModes()
    {
        var app = CreateApp(new AppSettings(), saveSettings: null);
        app.Session.Panels.LeftViewMode = PanelViewMode.BriefTwoColumns;
        app.Session.Panels.RightViewMode = PanelViewMode.Full;

        Execute(app, new MenuCommandRequest { CommandId = ApplicationCommandIds.SwapPanels });

        Assert.Equal(PanelViewMode.Full, app.Session.Panels.LeftViewMode);
        Assert.Equal(PanelViewMode.BriefTwoColumns, app.Session.Panels.RightViewMode);
    }

    [Fact]
    public void Application_FileAttributesMenuCommand_RedrawsClosedMenuBeforeOpeningDialog()
    {
        string path = Path.Combine(_tempDir, "file.txt");
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir, Item("file.txt", path));
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F9));
        driver.EnqueueKey(Key(ConsoleKey.LeftArrow));
        driver.EnqueueKey(Key(ConsoleKey.LeftArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialog = new InspectingAttributesDialog(driver);
        var app = CreateApp(
            new AppSettings(),
            saveSettings: null,
            fs,
            new RecordingMetadataService(Snapshot(path)),
            dialog,
            driver);
        app.Session.Panels.Left.CursorIndex = 1;

        app.Run();

        Assert.Equal(1, dialog.ShowCount);
        Assert.DoesNotContain("View", dialog.ScreenTextAtShow, StringComparison.Ordinal);
        Assert.DoesNotContain("Edit", dialog.ScreenTextAtShow, StringComparison.Ordinal);
        Assert.DoesNotContain("Attributes", dialog.ScreenTextAtShow, StringComparison.Ordinal);
    }

    private MenuBarDefinition BuildProviderMenu(bool canSaveSettings)
    {
        var left = new FilePanelState
        {
            CurrentDirectory = _tempDir,
            SortMode = SortMode.LastWriteTime,
            SortDescending = true,
        };
        var right = new FilePanelState { CurrentDirectory = _tempDir };
        var settings = new AppSettings();
        settings.Panels.Options.ShowHiddenAndSystemFiles = false;

        return new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = left,
            RightPanel = right,
            LeftViewMode = PanelViewMode.BriefTwoColumns,
            RightViewMode = PanelViewMode.Full,
            Settings = settings,
            CanSaveSettings = canSaveSettings,
            ModuleMenuItems =
            [
                new ModuleMenuProjection(
                    SftpModuleIds.MenuActionId,
                    "SFTP...",
                    'S'),
                new ModuleMenuProjection(
                    FtpModuleIds.MenuActionId,
                    "FTP/FTPS...",
                    'F'),
            ],
        });
    }

    private Application CreateApp(AppSettings settings, Action? saveSettings)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        return new Application(
            new ScreenRenderer(new FakeConsoleDriver()),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            saveSettings: saveSettings);
    }

    private Application CreateApp(
        AppSettings settings,
        Action? saveSettings,
        FakeFileSystemService fs,
        IFileMetadataService metadata,
        IFileAttributesDialog dialog,
        FakeConsoleDriver driver)
    {
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            saveSettings: saveSettings,
            fileMetadata: metadata,
            fileAttributesDialogFactory: () => dialog);
    }

    private static MenuCommandResult Execute(Application app, MenuCommandRequest request)
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        var context = GetCommandContext(app);
        return registry.Execute(request.CommandId, context, request.Args).ToMenuCommandResult();
    }

    private static CommandShortcutTextProvider ShortcutTextProvider() =>
        new(
            new DefaultKeyboardShortcutBindingProvider().GetBindings(),
            new DefaultFunctionKeyBindingProvider().GetBindings());

    private static ApplicationCommandContext GetCommandContext(Application app)
    {
        var field = typeof(Application).GetField(
            "_commandContext",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return (ApplicationCommandContext)field!.GetValue(app)!;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static FilePanelItem Item(string name, string path) =>
        new()
        {
            Name = name,
            FullPath = path,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static FileMetadataSnapshot Snapshot(string path) =>
        new(
            path,
            Path.GetFileName(path),
            false,
            FileAttributes.Archive,
            DateTime.Now,
            DateTime.Now,
            DateTime.Now,
            null,
            [new FileAttributeDescriptor(FileAttributeId.ReadOnly, "Read only", 'R', true, true)],
            new Dictionary<FileAttributeId, AttributeEditState>
            {
                [FileAttributeId.ReadOnly] = AttributeEditState.Unchecked,
            },
            true,
            true,
            true,
            null);

    private sealed class InspectingAttributesDialog(FakeConsoleDriver driver) : IFileAttributesDialog
    {
        public int ShowCount { get; private set; }
        public string ScreenTextAtShow { get; private set; } = string.Empty;

        public FileAttributesDialogResult? Show(FileMetadataSnapshot snapshot)
        {
            ShowCount++;
            ScreenTextAtShow = driver.GetRegionText(new Rect(0, 1, 14, 4));
            return null;
        }
    }

    private sealed class RecordingMetadataService(FileMetadataSnapshot snapshot) : IFileMetadataService
    {
        public FileMetadataSnapshot GetMetadata(string path) => snapshot;

        public FileMetadataSnapshot GetMergedMetadata(IReadOnlyList<string> paths) => snapshot;

        public FileMetadataApplyResult ApplyMetadata(IReadOnlyList<string> paths, FileMetadataChangeSet changes) =>
            new(paths.Count, paths.Count, []);
    }
}
