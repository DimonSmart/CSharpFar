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
                BuildPanelMenu("Left", PanelSide.Left, context.LeftPanel, context.LeftViewMode),
                BuildPanelMenu("Right", PanelSide.Right, context.RightPanel, context.RightViewMode),
                BuildOptionsMenu(context),
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
            HotKey = text[0],
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
                Command($"{id}.refresh", "Refresh", 'H',
                    MenuCommandIds.PanelRefresh,
                    new PanelCommandArgs { PanelSide = side },
                    Directory.Exists(panel.CurrentDirectory)),
            ],
        };
    }

    private static TopMenuItemDefinition BuildOptionsMenu(MenuBuildContext context)
    {
        var options = context.Settings.Panels.Options;
        var highlighting = context.Settings.Panels.FileHighlighting;

        List<MenuItemDefinition> children =
        [
            Command("Options.panelSettings", "Panel settings", 'P',
                MenuCommandIds.SettingsOpenPanelSettings),
            Separator("Options.sep.panelSettings"),
            CheckBox("Options.showHiddenAndSystemFiles", "Show hidden and system files", 'H',
                MenuCommandIds.SettingsToggleShowHiddenAndSystemFiles, null,
                options.ShowHiddenAndSystemFiles),
            CheckBox("Options.highlightFiles", "Highlight files", 'L',
                MenuCommandIds.SettingsToggleHighlightFiles, null,
                highlighting.Enabled),
            CheckBox("Options.selectFolders", "Select folders", 'F',
                MenuCommandIds.SettingsToggleSelectFolders, null,
                options.SelectFolders),
            CheckBox("Options.rightClickSelectsFiles", "Right click selects files", 'R',
                MenuCommandIds.SettingsToggleRightClickSelectsFiles, null,
                options.RightClickSelectsFiles),
            CheckBox("Options.sortFoldersByExtension", "Sort folders by extension", 'X',
                MenuCommandIds.SettingsToggleSortFoldersByExtension, null,
                options.SortFoldersByExtension),
            CheckBox("Options.showStatusLine", "Show status line", 'U',
                MenuCommandIds.SettingsToggleShowStatusLine, null,
                options.ShowStatusLine),
            CheckBox("Options.showFilesTotalInformation", "Show files total information", 'I',
                MenuCommandIds.SettingsToggleShowFilesTotalInformation, null,
                options.ShowFilesTotalInformation),
            CheckBox("Options.showFreeSize", "Show free size", 'Z',
                MenuCommandIds.SettingsToggleShowFreeSize, null,
                options.ShowFreeSize),
            CheckBox("Options.showScrollbar", "Show scrollbar", 'C',
                MenuCommandIds.SettingsToggleShowScrollbar, null,
                options.ShowScrollbar),
            CheckBox("Options.showSortModeLetter", "Show sort mode letter", 'M',
                MenuCommandIds.SettingsToggleShowSortModeLetter, null,
                options.ShowSortModeLetter),
            CheckBox("Options.showParentDirectoryInRootFolders", "Show \"..\" in root folders", 'D',
                MenuCommandIds.SettingsToggleShowParentDirectoryInRootFolders, null,
                options.ShowParentDirectoryInRootFolders),
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
            HotKey = 'O',
            Children = children,
        };
    }

    private static MenuItemDefinition Command(
        string id,
        string text,
        char hotKey,
        string commandId,
        object? args = null,
        bool isEnabled = true) =>
        new()
        {
            Id = id,
            Text = text,
            HotKey = hotKey,
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
