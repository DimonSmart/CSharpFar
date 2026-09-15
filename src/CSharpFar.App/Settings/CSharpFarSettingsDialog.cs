using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Settings;

internal sealed record CSharpFarPanelSettings(
    PanelViewMode LeftViewMode,
    PanelViewMode RightViewMode,
    bool ShowHiddenAndSystemFiles,
    bool SelectFolders,
    bool RightClickSelectsFiles,
    bool SortFoldersByExtension,
    bool RememberLastDirectories,
    bool FileHighlightingEnabled,
    bool ShowStatusLine,
    bool ShowFilesTotalInformation,
    bool ShowFreeSize,
    bool ShowSortModeLetter,
    bool ShowParentDirectoryInRootFolders);

internal sealed record CSharpFarSettingsDialogResult(
    CSharpFarPanelSettings Panels,
    string PaletteName,
    bool EditorSyntaxHighlightingEnabled);

internal sealed class CSharpFarSettingsDialog
{
    private static readonly PanelViewMode[] ViewModes = [PanelViewMode.Full, PanelViewMode.BriefTwoColumns];
    private static readonly string[] PaletteNames = [.. CSharpFarPaletteRegistry.Names];

    private readonly DialogService _dialogs;

    public CSharpFarSettingsDialog(DialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public CSharpFarSettingsDialogResult? Show(
        CSharpFarPanelSettings panels,
        string paletteName,
        bool editorSyntaxHighlightingEnabled)
    {
        ArgumentNullException.ThrowIfNull(panels);

        var leftViewMode = FormControls.CompactChoice(
            "panels.left-view-mode",
            "Left panel view",
            ViewModes,
            ViewModeLabel,
            panels.LeftViewMode);
        var rightViewMode = FormControls.CompactChoice(
            "panels.right-view-mode",
            "Right panel view",
            ViewModes,
            ViewModeLabel,
            panels.RightViewMode);
        var showHiddenAndSystemFiles = FormControls.CheckBox(
            "panels.show-hidden-and-system-files",
            "Show hidden and system files",
            panels.ShowHiddenAndSystemFiles);
        var selectFolders = FormControls.CheckBox(
            "panels.select-folders",
            "Allow folders to be selected",
            panels.SelectFolders);
        var rightClickSelectsFiles = FormControls.CheckBox(
            "panels.right-click-selects-files",
            "Right-click selects/deselects items",
            panels.RightClickSelectsFiles);
        var sortFoldersByExtension = FormControls.CheckBox(
            "panels.sort-folders-by-extension",
            "Sort folders by extension",
            panels.SortFoldersByExtension);
        var rememberDirectories = FormControls.CheckBox(
            "panels.remember-directories",
            "Restore last opened panel folders on startup",
            panels.RememberLastDirectories);
        var fileHighlighting = FormControls.CheckBox(
            "panels.file-highlighting",
            "Enable file highlighting",
            panels.FileHighlightingEnabled);
        var showStatusLine = FormControls.CheckBox(
            "panels.show-status-line",
            "Show panel status line",
            panels.ShowStatusLine);
        var showFilesTotalInformation = FormControls.CheckBox(
            "panels.show-files-total-information",
            "Show file count and total size",
            panels.ShowFilesTotalInformation);
        var showFreeSize = FormControls.CheckBox(
            "panels.show-free-size",
            "Show free disk space",
            panels.ShowFreeSize);
        var showSortModeLetter = FormControls.CheckBox(
            "panels.show-sort-mode-letter",
            "Show current sort mode indicator",
            panels.ShowSortModeLetter);
        var showParentDirectoryInRootFolders = FormControls.CheckBox(
            "panels.show-parent-directory-in-root-folders",
            "Show \"..\" entry in filesystem roots",
            panels.ShowParentDirectoryInRootFolders);
        var palette = FormControls.CompactChoice(
            "appearance.palette",
            "Palette",
            PaletteNames,
            static name => name,
            paletteName,
            StringComparer.OrdinalIgnoreCase);
        var syntaxHighlighting = FormControls.CheckBox(
            "editor.syntax-highlighting",
            "Syntax highlighting",
            editorSyntaxHighlightingEnabled);

        SettingsPage[] pages =
        [
            new SettingsPage(
                "panels",
                "Panels",
                [
                    FormControls.Label("View"),
                    leftViewMode,
                    rightViewMode,
                    showStatusLine,
                    showFilesTotalInformation,
                    showFreeSize,
                    showSortModeLetter,
                    FormControls.Spacer(),
                    FormControls.Label("Files and folders"),
                    showHiddenAndSystemFiles,
                    fileHighlighting,
                    sortFoldersByExtension,
                    showParentDirectoryInRootFolders,
                    FormControls.Spacer(),
                    FormControls.Label("Interaction"),
                    selectFolders,
                    rightClickSelectsFiles,
                    rememberDirectories,
                ]),
            new SettingsPage(
                "appearance",
                "Appearance",
                [palette]),
            new SettingsPage(
                "editor",
                "Editor",
                [syntaxHighlighting]),
        ];

        SettingsDialogResult lifecycle = _dialogs.Settings(
            new SettingsDialogOptions("Settings")
            {
                Theme = () => CSharpFarPaletteRegistry.Resolve(palette.Value).Ui,
            },
            pages);

        if (lifecycle != SettingsDialogResult.Saved)
            return null;

        return new CSharpFarSettingsDialogResult(
            new CSharpFarPanelSettings(
                leftViewMode.Value,
                rightViewMode.Value,
                showHiddenAndSystemFiles.Value,
                selectFolders.Value,
                rightClickSelectsFiles.Value,
                sortFoldersByExtension.Value,
                rememberDirectories.Value,
                fileHighlighting.Value,
                showStatusLine.Value,
                showFilesTotalInformation.Value,
                showFreeSize.Value,
                showSortModeLetter.Value,
                showParentDirectoryInRootFolders.Value),
            palette.Value,
            syntaxHighlighting.Value);
    }

    private static string ViewModeLabel(PanelViewMode mode) =>
        mode == PanelViewMode.BriefTwoColumns ? "Brief (2 cols)" : "Full";
}
