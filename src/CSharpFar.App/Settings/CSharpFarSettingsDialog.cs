using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Settings;

internal sealed record CSharpFarSettingsDialogResult(
    PanelViewMode LeftViewMode,
    PanelViewMode RightViewMode,
    string PaletteName,
    bool FileHighlightingEnabled,
    bool EditorSyntaxHighlightingEnabled,
    bool RememberLastDirectories);

internal sealed class CSharpFarSettingsDialog
{
    private static readonly PanelViewMode[] ViewModes = [PanelViewMode.Full, PanelViewMode.BriefTwoColumns];
    private static readonly string[] PaletteNames = [.. CSharpFarPaletteRegistry.Names];

    private readonly DialogService _dialogs;

    public CSharpFarSettingsDialog(DialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public CSharpFarSettingsDialogResult? Show(
        PanelViewMode leftMode,
        PanelViewMode rightMode,
        string paletteName,
        bool fileHighlightingEnabled,
        bool editorSyntaxHighlightingEnabled,
        bool rememberLastDirectories = false)
    {
        var leftViewMode = FormControls.CompactChoice(
            "panels.left-view-mode",
            "Left panel",
            ViewModes,
            ViewModeLabel,
            leftMode);
        var rightViewMode = FormControls.CompactChoice(
            "panels.right-view-mode",
            "Right panel",
            ViewModes,
            ViewModeLabel,
            rightMode);
        var fileHighlighting = FormControls.CheckBox(
            "panels.file-highlighting",
            "File highlighting",
            fileHighlightingEnabled);
        var rememberDirectories = FormControls.CheckBox(
            "panels.remember-directories",
            "Remember last panel folders",
            rememberLastDirectories);
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
                    leftViewMode,
                    rightViewMode,
                    fileHighlighting,
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
            leftViewMode.Value,
            rightViewMode.Value,
            palette.Value,
            fileHighlighting.Value,
            syntaxHighlighting.Value,
            rememberDirectories.Value);
    }

    private static string ViewModeLabel(PanelViewMode mode) =>
        mode == PanelViewMode.BriefTwoColumns ? "Brief (2 cols)" : "Full";
}
