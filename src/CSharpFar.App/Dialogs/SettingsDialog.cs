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
    private const int DialogWidth = 44;
    private const int DialogHeight = 15;
    private const string LeftViewModeRowId = "settings.left-view-mode";
    private const string RightViewModeRowId = "settings.right-view-mode";
    private const string PaletteRowId = "settings.palette";
    private const string FileHighlightingRowId = "settings.file-highlighting";
    private const string EditorSyntaxHighlightingRowId = "settings.editor-syntax-highlighting";

    private static readonly PanelViewMode[] ViewModes = [PanelViewMode.Full, PanelViewMode.BriefTwoColumns];
    private static readonly string[] PaletteNames = [.. PaletteRegistry.Names];

    private readonly ModalFormHost _formDialogs;

    public SettingsDialog(ModalDialogHost modalDialogs)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
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
        var leftViewMode = FormControls.CompactChoice(LeftViewModeRowId, "Left panel", ViewModes, ViewModeLabel, leftMode);
        var rightViewMode = FormControls.CompactChoice(RightViewModeRowId, "Right panel", ViewModes, ViewModeLabel, rightMode);
        var palette = FormControls.CompactChoice(PaletteRowId, "Palette", PaletteNames, static name => name, paletteName, StringComparer.OrdinalIgnoreCase);
        var fileHighlighting = FormControls.CheckBox(FileHighlightingRowId, "File highlighting", fileHighlightingEnabled);
        var syntaxHighlighting = FormControls.CheckBox(EditorSyntaxHighlightingRowId, "Editor syntax highlighting", editorSyntaxHighlightingEnabled);
        var form = new ScrollableFormDialog(
            new FormLayoutOptions(CursorPolicy: FormCursorPolicy.Hidden));

        void PrepareRows() =>
            form.SetRows(
                [
                    leftViewMode,
                    rightViewMode,
                    palette,
                    fileHighlighting,
                    syntaxHighlighting,
                    new SpacerRow(),
                    new LabelRow("Enter/Space  change value", FarDialogStyles.Fill),
                    new LabelRow("Up/Down      select item", FarDialogStyles.Fill),
                    new LabelRow("F10          save & close", FarDialogStyles.Fill),
                    new LabelRow("Esc          close", FarDialogStyles.Fill),
                    new SpacerRow(),
                ]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions("Settings", DialogWidth, DialogHeight),
            static layout => ModalFormLayout.BodyOnly(layout.ContentBounds),
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<SettingsDialogResult?>.Complete(null);

                if (FormDialogInput.ShouldSubmit(routed, result, form))
                {
                    return ModalDialogLoopResult<SettingsDialogResult?>.Complete(new SettingsDialogResult(
                        leftViewMode.Value,
                        rightViewMode.Value,
                        palette.Value,
                        fileHighlighting.Value,
                        syntaxHighlighting.Value));
                }

                return ModalDialogLoopResult<SettingsDialogResult?>.ContinueNoChange;
            },
            prepareRender: PrepareRows,
            beginRenderScope: () => UiTheme.UseTemporary(PaletteRegistry.Resolve(palette.Value)));
    }

    private static string ViewModeLabel(PanelViewMode mode) => mode switch
    {
        PanelViewMode.BriefTwoColumns => "Brief two columns",
        _ => "Full",
    };

}
