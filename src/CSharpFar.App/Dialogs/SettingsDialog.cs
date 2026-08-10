using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>Result returned by SettingsDialog when the user saves (F10).</summary>
public sealed record SettingsDialogResult(
    PanelViewMode LeftViewMode,
    PanelViewMode RightViewMode,
    string PaletteName,
    bool FileHighlightingEnabled,
    bool EditorSyntaxHighlightingEnabled);

/// <summary>
/// Modal settings window.
/// Enter/Space cycles the value of the focused item.
/// F10 saves and closes; Esc closes without saving.
/// </summary>
internal sealed class SettingsDialog
{

    private static readonly PanelViewMode[] ViewModes = [PanelViewMode.Full, PanelViewMode.BriefTwoColumns];
    private static readonly string[] PaletteNames = [.. PaletteRegistry.Names];

    private readonly DialogService _dialogs;

    public SettingsDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    /// <summary>
    /// Shows the settings dialog. Returns new settings on F10, null on Esc.
    /// </summary>
    public SettingsDialogResult? Show(
        PanelViewMode leftMode,
        PanelViewMode rightMode,
        string paletteName,
        bool fileHighlightingEnabled,
        bool editorSyntaxHighlightingEnabled)
    {
        var leftViewMode = FormControls.CompactChoice("Left panel", ViewModes, ViewModeLabel, leftMode);
        var rightViewMode = FormControls.CompactChoice("Right panel", ViewModes, ViewModeLabel, rightMode);
        var palette = FormControls.CompactChoice("Palette", PaletteNames, static name => name, paletteName, StringComparer.OrdinalIgnoreCase);
        var fileHighlighting = FormControls.CheckBox("File highlighting", fileHighlightingEnabled);
        var syntaxHighlighting = FormControls.CheckBox("Editor syntax highlighting", editorSyntaxHighlightingEnabled);
        return _dialogs.Form(
            new FormDialogOptions("Settings")
            {
                Layout = new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden),
                Theme = () => PaletteRegistry.Resolve(palette.Value),
            },
            rows: () =>
                [
                    leftViewMode,
                    rightViewMode,
                    palette,
                    fileHighlighting,
                    syntaxHighlighting,
                    FormControls.Spacer(),
                    FormControls.Label("Enter/Space  change value"),
                    FormControls.Label("Up/Down      select item"),
                    FormControls.Label("F10          save & close"),
                    FormControls.Label("Esc          close"),
                    FormControls.Spacer(),
                ],
            submit: () => FormSubmit.Success(new SettingsDialogResult(
                leftViewMode.Value,
                rightViewMode.Value,
                palette.Value,
                fileHighlighting.Value,
                syntaxHighlighting.Value)));
    }

    private static string ViewModeLabel(PanelViewMode mode) => mode switch
    {
        PanelViewMode.BriefTwoColumns => "Brief two columns",
        _ => "Full",
    };

}
