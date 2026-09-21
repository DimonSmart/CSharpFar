using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Modules;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Menu;

public sealed class DefaultMenuDefinitionProvider
{
    public MenuBarDefinition BuildMenu(MenuBuildContext context) =>
        new()
        {
            Items =
            [
                BuildFileMenu(),
                BuildCommandsMenu(),
                BuildPanelMenu("Left", PanelSide.Left, context.LeftPanel, context.LeftViewMode),
                BuildPanelMenu("Right", PanelSide.Right, context.RightPanel, context.RightViewMode),
                BuildModuleMenu(context.ModuleMenuItems),
                BuildOptionsMenu(context),
                BuildHelpMenu(),
            ],
        };

    private static TopMenuItemDefinition BuildFileMenu() =>
        new()
        {
            Id = "File",
            Text = "File",
            HotChar = 'F',
            Children =
            [
                Command("File.view", "View", 'V', FunctionKeyCommandIds.View),
                Command("File.edit", "Edit", 'E', FunctionKeyCommandIds.Edit),
                Command("File.attributes", "Attributes", 'A', FunctionKeyCommandIds.Attributes),
            ],
        };

    private static TopMenuItemDefinition BuildCommandsMenu() =>
        new()
        {
            Id = "Commands",
            Text = "Commands",
            HotChar = 'C',
            Children =
            [
                Command(
                    "Commands.togglePanels",
                    "Panels on/off",
                    'P',
                    ApplicationCommandIds.TogglePanels),
                Command(
                    "Commands.swapPanels",
                    "Swap panels",
                    'S',
                    ApplicationCommandIds.SwapPanels),
                Separator("Commands.sep.compare"),
                Command(
                    "Commands.compareFolders",
                    "Compare folders",
                    'F',
                    ApplicationCommandIds.CompareFolders),
                Command(
                    "Commands.compareFileSets",
                    "Compare file sets",
                    'E',
                    ApplicationCommandIds.CompareFileSets),
            ],
        };

    private static TopMenuItemDefinition BuildPanelMenu(
        string text,
        PanelSide side,
        FilePanelState panel,
        PanelViewMode viewMode)
    {
        string id = text;

        return new TopMenuItemDefinition
        {
            Id = id,
            Text = text,
            HotChar = text[0],
            Children =
            [
                Radio($"{id}.full", "Full mode", 'F',
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs { PanelSide = side, ViewMode = PanelViewMode.Full },
                    viewMode == PanelViewMode.Full),
                Radio($"{id}.brief", "Brief mode", 'B',
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs { PanelSide = side, ViewMode = PanelViewMode.BriefTwoColumns },
                    viewMode == PanelViewMode.BriefTwoColumns),
                Separator($"{id}.sep.view"),
                Radio($"{id}.sort.name", "Sort by name", 'N',
                    MenuCommandIds.PanelSetSortMode,
                    new SetPanelSortModeArgs { PanelSide = side, SortMode = SortMode.Name },
                    panel.SortMode == SortMode.Name),
                Radio($"{id}.sort.extension", "Sort by extension", 'E',
                    MenuCommandIds.PanelSetSortMode,
                    new SetPanelSortModeArgs { PanelSide = side, SortMode = SortMode.Extension },
                    panel.SortMode == SortMode.Extension),
                Radio($"{id}.sort.size", "Sort by size", 'S',
                    MenuCommandIds.PanelSetSortMode,
                    new SetPanelSortModeArgs { PanelSide = side, SortMode = SortMode.Size },
                    panel.SortMode == SortMode.Size),
                Radio($"{id}.sort.lastWriteTime", "Sort by last write time", 'T',
                    MenuCommandIds.PanelSetSortMode,
                    new SetPanelSortModeArgs { PanelSide = side, SortMode = SortMode.LastWriteTime },
                    panel.SortMode == SortMode.LastWriteTime),
                Separator($"{id}.sep.sort"),
                CheckBox($"{id}.reverseSort", "Reverse sort", 'R',
                    MenuCommandIds.PanelToggleReverseSort,
                    new PanelCommandArgs { PanelSide = side },
                    panel.SortDescending),
                Separator($"{id}.sep.refresh"),
                Command($"{id}.refresh", panel.LoadError is null ? "Refresh" : "Retry",
                    panel.LoadError is null ? 'H' : 'Y',
                    MenuCommandIds.PanelRefresh,
                    new PanelCommandArgs { PanelSide = side },
                    (panel.ProviderCapabilities & PanelProviderCapabilities.Refresh) == PanelProviderCapabilities.Refresh &&
                    (panel.LoadError is not null ||
                     panel.SearchRequest is not null ||
                     (panel.ContentKind == PanelContentKind.Source &&
                      (panel.SourceId != PanelSourceId.Local ||
                       Directory.Exists(panel.CurrentDirectory))))),
            ],
        };
    }

    private static TopMenuItemDefinition BuildModuleMenu(
        IReadOnlyList<ModuleMenuProjection> moduleMenuItems)
    {
        var children = moduleMenuItems
            .Select(item => Command(
                $"Plugins.Module.{item.ActionId:D}",
                item.Text,
                item.HotKey,
                MenuCommandIds.ModuleOpen,
                new ModuleOpenCommandArgs(item.ActionId)))
            .ToList();

        if (children.Count == 0)
            children.Add(Separator("Plugins.empty"));

        return new TopMenuItemDefinition
        {
            Id = "Plugins",
            Text = "Plugins",
            HotChar = 'P',
            Children = children,
        };
    }

    private static TopMenuItemDefinition BuildOptionsMenu(MenuBuildContext context)
    {
        List<MenuItemDefinition> children =
        [
            Command("Options.panelSettings", "Settings...", 'E',
                MenuCommandIds.SettingsOpenPanelSettings),
            Command("Options.directoryShortcuts", "Directory shortcuts...", 'T',
                MenuCommandIds.SettingsOpenDirectoryShortcuts),
            Command("Options.userMenu", "User menu...", 'N',
                MenuCommandIds.SettingsOpenUserMenu),
            Separator("Options.sep.panelSettings"),
            Command("Options.diagnostics", "Terminal diagnostics", 'G',
                MenuCommandIds.DiagnosticsPrintTerminalInfo),
        ];

        if (context.CanSaveSettings)
        {
            children.Add(Separator("Options.sep.save"));
            children.Add(Command("Options.save", "Save setup", 'S', MenuCommandIds.SettingsSave));
        }

        return new TopMenuItemDefinition
        {
            Id = "Options",
            Text = "Options",
            HotChar = 'O',
            Children = children,
        };
    }

    private static TopMenuItemDefinition BuildHelpMenu() =>
        new()
        {
            Id = "Help",
            Text = "Help",
            HotChar = 'H',
            Children =
            [
                Command("Help.help", "Help", 'H', FunctionKeyCommandIds.Help),
                Separator("Help.sep.about"),
                Command("Help.about", "About...", 'A', ApplicationCommandIds.About),
                Command("Help.diagnostics", "Diagnostics...", 'D', ApplicationCommandIds.Diagnostics),
            ],
        };

    private static MenuItemDefinition Command(
        string id,
        string text,
        char? hotKey,
        string commandId,
        object? args = null,
        bool isEnabled = true) =>
        new()
        {
            Id = id,
            Text = text,
            HotChar = hotKey,
            CommandId = commandId,
            CommandArgs = args,
            IsEnabled = isEnabled,
        };

    private static MenuItemDefinition CheckBox(
        string id,
        string text,
        char hotKey,
        string commandId,
        object? args,
        bool isChecked,
        bool isEnabled = true) =>
        Command(id, text, hotKey, commandId, args, isEnabled) with
        {
            Kind = MenuItemKind.CheckBox,
            IsChecked = isChecked,
        };

    private static MenuItemDefinition Radio(
        string id,
        string text,
        char hotKey,
        string commandId,
        object args,
        bool isChecked,
        bool isEnabled = true) =>
        Command(id, text, hotKey, commandId, args, isEnabled) with
        {
            Kind = MenuItemKind.Radio,
            IsChecked = isChecked,
        };

    private static MenuItemDefinition Separator(string id) =>
        new()
        {
            Id = id,
            Text = string.Empty,
            Kind = MenuItemKind.Separator,
            IsEnabled = false,
        };
}
