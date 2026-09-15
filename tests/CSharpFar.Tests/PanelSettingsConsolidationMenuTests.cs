using CSharpFar.App.Menu;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class PanelSettingsConsolidationMenuTests
{
    [Fact]
    public void Options_ContainsUnifiedSettingsAndNoPanelSettingToggles()
    {
        var state = new FilePanelState { CurrentDirectory = Path.GetTempPath() };
        var menu = new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = state,
            RightPanel = state,
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.Full,
            Settings = new AppSettings(),
            CanSaveSettings = true,
        });

        IReadOnlyList<MenuItemDefinition> options = menu.Items.Single(item => item.Id == "Options").Children;

        var settings = options.Single(item => item.Id == "Options.panelSettings");
        Assert.Equal("Settings...", settings.Text);
        Assert.Equal(MenuCommandIds.SettingsOpenPanelSettings, settings.CommandId);

        string[] removedIds =
        [
            "Options.showHiddenAndSystemFiles",
            "Options.highlightFiles",
            "Options.selectFolders",
            "Options.rightClickSelectsFiles",
            "Options.sortFoldersByExtension",
            "Options.showStatusLine",
            "Options.showFilesTotalInformation",
            "Options.showFreeSize",
            "Options.showSortModeLetter",
            "Options.showParentDirectoryInRootFolders",
        ];
        Assert.DoesNotContain(options, item => removedIds.Contains(item.Id, StringComparer.Ordinal));

        Assert.Contains(options, item => item.Text == "Directory shortcuts...");
        Assert.Contains(options, item => item.Text == "User menu...");
        Assert.Contains(options, item => item.Text == "Terminal diagnostics");
        Assert.Contains(options, item => item.Text == "Save setup");
    }
}
