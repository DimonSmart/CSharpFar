using CSharpFar.App.Rendering;
using CSharpFar.App.Settings;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenSettingsCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenPanelSettings;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var options = context.Settings.Panels.Options;
        var panelSettings = new CSharpFarPanelSettings(
            context.LeftViewMode,
            context.RightViewMode,
            options.ShowHiddenAndSystemFiles,
            options.SelectFolders,
            options.RightClickSelectsFiles,
            options.SortFoldersByExtension,
            options.RememberLastDirectories,
            context.Settings.Panels.FileHighlighting.Enabled,
            options.ShowStatusLine,
            options.ShowFilesTotalInformation,
            options.ShowFreeSize,
            options.ShowSortModeLetter,
            options.ShowParentDirectoryInRootFolders);

        var result = new CSharpFarSettingsDialog(context.Dialogs).Show(
            panelSettings,
            context.Settings.Ui.Palette,
            context.Settings.Editor.SyntaxHighlightingEnabled);

        if (result is null)
            return ApplicationCommandResult.Rendered();

        CSharpFarPanelSettings panels = result.Panels;
        context.LeftViewMode = panels.LeftViewMode;
        context.RightViewMode = panels.RightViewMode;
        context.Settings.Panels.LeftViewMode = panels.LeftViewMode.ToString();
        context.Settings.Panels.RightViewMode = panels.RightViewMode.ToString();
        options.ShowHiddenAndSystemFiles = panels.ShowHiddenAndSystemFiles;
        options.SelectFolders = panels.SelectFolders;
        options.RightClickSelectsFiles = panels.RightClickSelectsFiles;
        options.SortFoldersByExtension = panels.SortFoldersByExtension;
        options.RememberLastDirectories = panels.RememberLastDirectories;
        context.Settings.Panels.FileHighlighting.Enabled = panels.FileHighlightingEnabled;
        options.ShowStatusLine = panels.ShowStatusLine;
        options.ShowFilesTotalInformation = panels.ShowFilesTotalInformation;
        options.ShowFreeSize = panels.ShowFreeSize;
        options.ShowSortModeLetter = panels.ShowSortModeLetter;
        options.ShowParentDirectoryInRootFolders = panels.ShowParentDirectoryInRootFolders;
        context.Settings.Ui.Palette = result.PaletteName;
        context.Settings.Editor.SyntaxHighlightingEnabled = result.EditorSyntaxHighlightingEnabled;

        context.CommandPalette = CSharpFarPaletteRegistry.Resolve(result.PaletteName);
        context.HighlightService = context.CreateHighlightService();
        context.RefreshPanels();
        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }
}
