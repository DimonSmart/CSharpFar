using CSharpFar.App.Highlighting;
using CSharpFar.App.State;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.App.Editor;
using CSharpFar.App.Rendering;
using CSharpFar.App.Settings;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenSettingsCommand(AppSettingsAlias settings, PanelSessionState panelsState,
    ApplicationState appState, DialogService dialogs, FileHighlightState highlightState,
    PanelSettingsChangeService settingsChange) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenPanelSettings;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var options = settings.Panels.Options;
        var panelSettings = new CSharpFarPanelSettings(
            panelsState.LeftViewMode,
            panelsState.RightViewMode,
            options.ShowHiddenAndSystemFiles,
            options.SelectFolders,
            options.RightClickSelectsFiles,
            options.SortFoldersByExtension,
            options.RememberLastDirectories,
            settings.Panels.FileHighlighting.Enabled,
            options.ShowStatusLine,
            options.ShowFilesTotalInformation,
            options.ShowFreeSize,
            options.ShowSortModeLetter,
            options.ShowParentDirectoryInRootFolders);

        var syntaxCatalog = new TextMateEditorSyntaxCatalog(settings.Editor);
        var result = new CSharpFarSettingsDialog(dialogs).Show(
            settings.Application.ConfirmExit,
            panelSettings,
            settings.Ui.Palette,
            settings.Editor.SyntaxHighlightingEnabled,
            settings.Editor.SyntaxTheme,
            syntaxCatalog,
            preferredMacOsTerminal: settings.Application.PreferredMacOsTerminal);

        if (result is null)
            return ApplicationCommandResult.Rendered();

        settings.Application.ConfirmExit = result.ApplicationConfirmExit;
        settings.Application.PreferredMacOsTerminal = result.PreferredMacOsTerminal;

        CSharpFarPanelSettings panels = result.Panels;
        panelsState.LeftViewMode = panels.LeftViewMode;
        panelsState.RightViewMode = panels.RightViewMode;
        settings.Panels.LeftViewMode = panels.LeftViewMode.ToString();
        settings.Panels.RightViewMode = panels.RightViewMode.ToString();
        options.ShowHiddenAndSystemFiles = panels.ShowHiddenAndSystemFiles;
        options.SelectFolders = panels.SelectFolders;
        options.RightClickSelectsFiles = panels.RightClickSelectsFiles;
        options.SortFoldersByExtension = panels.SortFoldersByExtension;
        options.RememberLastDirectories = panels.RememberLastDirectories;
        settings.Panels.FileHighlighting.Enabled = panels.FileHighlightingEnabled;
        options.ShowStatusLine = panels.ShowStatusLine;
        options.ShowFilesTotalInformation = panels.ShowFilesTotalInformation;
        options.ShowFreeSize = panels.ShowFreeSize;
        options.ShowSortModeLetter = panels.ShowSortModeLetter;
        options.ShowParentDirectoryInRootFolders = panels.ShowParentDirectoryInRootFolders;
        settings.Ui.Palette = result.PaletteName;
        settings.Editor.SyntaxHighlightingEnabled = result.EditorSyntaxHighlightingEnabled;
        settings.Editor.SyntaxTheme = result.EditorSyntaxTheme;

        appState.Palette = CSharpFarPaletteRegistry.Resolve(result.PaletteName);
        UiTheme.Initialize(appState.Palette.Ui);
        highlightState.Current = FileHighlightServiceFactory.Create(settings);
        settingsChange.Apply();
        return ApplicationCommandResult.Rendered();
    }
}
