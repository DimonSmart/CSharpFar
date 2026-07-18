using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SettingsDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
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
        var leftViewMode = new CompactChoiceFormRow<PanelViewMode>(
            new ChoiceRow<PanelViewMode>(ViewModes, ViewModeLabel, ViewModeIndex(leftMode)),
            "Left panel")
        {
            Id = LeftViewModeRowId,
            ShowCursor = false,
        };
        var rightViewMode = new CompactChoiceFormRow<PanelViewMode>(
            new ChoiceRow<PanelViewMode>(ViewModes, ViewModeLabel, ViewModeIndex(rightMode)),
            "Right panel")
        {
            Id = RightViewModeRowId,
            ShowCursor = false,
        };
        var palette = new CompactChoiceFormRow<string>(
            new ChoiceRow<string>(PaletteNames, static name => name, FindPaletteIndexOrDefault(paletteName)),
            "Palette")
        {
            Id = PaletteRowId,
            ShowCursor = false,
        };
        var fileHighlighting = new CheckBoxRow(new CheckBoxLine("File highlighting"))
        {
            Id = FileHighlightingRowId,
            Value = fileHighlightingEnabled,
            ShowCursor = false,
        };
        var syntaxHighlighting = new CheckBoxRow(new CheckBoxLine("Editor syntax highlighting"))
        {
            Id = EditorSyntaxHighlightingRowId,
            Value = editorSyntaxHighlightingEnabled,
            ShowCursor = false,
        };
        var form = new ScrollableFormDialog();

        void PrepareRows() =>
            form.SetRows(
                [
                    leftViewMode,
                    rightViewMode,
                    palette,
                    fileHighlighting,
                    syntaxHighlighting,
                    new SeparatorRow(FarDialogStyles.Border, drawLine: false),
                    new LabelRow("Enter/Space  change value", FarDialogStyles.Fill),
                    new LabelRow("Up/Down      select item", FarDialogStyles.Fill),
                    new LabelRow("F10          save & close", FarDialogStyles.Fill),
                    new LabelRow("Esc          close", FarDialogStyles.Fill),
                    new SeparatorRow(FarDialogStyles.Border, drawLine: false),
                ]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, SettingsDialogResult?>(
            (context, focusScope) => Draw(context, focusScope, form, () => palette.Value),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<SettingsDialogResult?>.Complete(null);

                if (routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
                {
                    return ModalDialogLoopResult<SettingsDialogResult?>.Complete(new SettingsDialogResult(
                        leftViewMode.Value,
                        rightViewMode.Value,
                        palette.Value,
                        fileHighlighting.Value,
                        syntaxHighlighting.Value));
                }

                return ModalDialogLoopResult<SettingsDialogResult?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private ScrollableFormFrame Draw(
        UiRenderContext context,
        UiFocusScope focusScope,
        ScrollableFormDialog form,
        Func<string> paletteName)
    {
        ScrollableFormFrame? frame = null;
        using (UiTheme.UseTemporary(PaletteRegistry.Resolve(paletteName())))
        {
            _modalRenderer.Render(
                context.Screen,
                OuterBounds(context.Size),
                "Settings",
                doubleBorder: true,
                FarDialogStyles.OuterOptions,
                FarDialogStyles.FrameOptions,
                (_, layout) =>
                {
                    Rect contentBounds = layout.ContentBounds;
                    var bodyBounds = contentBounds.Width >= 2
                        ? new Rect(contentBounds.X + 1, contentBounds.Y, contentBounds.Width - 2, contentBounds.Height)
                        : new Rect(contentBounds.X, contentBounds.Y, 0, 0);
                    frame = form.Render(new FormRenderContext(context, bodyBounds, FarDialogStyles.Border), focusScope);
                });
        }

        return frame ?? throw new InvalidOperationException("Settings dialog did not render a form frame.");
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        return new ModalDialogRenderer().CenteredOuterBounds(size, DialogWidth, DialogHeight);
    }

    private static string ViewModeLabel(PanelViewMode mode) => mode switch
    {
        PanelViewMode.BriefTwoColumns => "Brief two columns",
        _ => "Full",
    };

    private static int ViewModeIndex(PanelViewMode mode)
    {
        int index = Array.IndexOf(ViewModes, mode);
        return index < 0 ? 0 : index;
    }

    private static int FindPaletteIndexOrDefault(string paletteName)
    {
        int index = Array.FindIndex(PaletteNames,
            name => string.Equals(name, paletteName, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }
}
