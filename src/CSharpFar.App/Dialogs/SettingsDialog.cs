using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>Result returned by SettingsDialog when the user saves (F10).</summary>
public sealed record SettingsDialogResult(
    PanelViewMode LeftViewMode,
    PanelViewMode RightViewMode,
    string        PaletteName,
    bool          FileHighlightingEnabled);

/// <summary>
/// Modal settings window.
/// Enter/Space cycles the value of the focused item.
/// F10 saves and closes; Esc closes without saving.
/// </summary>
internal sealed class SettingsDialog
{
    private const int DialogWidth  = 44;
    private const int DialogHeight = 14;

    private static readonly PanelViewMode[] ViewModes    = [PanelViewMode.Full, PanelViewMode.BriefTwoColumns];
    private static readonly string[]        PaletteNames = [.. PaletteRegistry.Names];

    private readonly ScreenRenderer _screen;

    public SettingsDialog(ScreenRenderer screen) => _screen = screen;

    /// <summary>
    /// Shows the settings dialog. Returns new settings on F10, null on Esc.
    /// </summary>
    public SettingsDialogResult? Show(
        PanelViewMode leftMode,
        PanelViewMode rightMode,
        string        paletteName,
        bool          fileHighlightingEnabled)
    {
        var size   = _screen.GetSize();
        var region = new Rect(0, 0, size.Width, size.Height);
        var saved  = _screen.Capture(region);

        try
        {
            return RunLoop(size, leftMode, rightMode, paletteName, fileHighlightingEnabled);
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    // ── dialog loop ───────────────────────────────────────────────────────────

    private SettingsDialogResult? RunLoop(
        ConsoleSize   screenSize,
        PanelViewMode leftMode,
        PanelViewMode rightMode,
        string        paletteName,
        bool          hlEnabled)
    {
        int dialogX = (screenSize.Width  - DialogWidth)  / 2;
        int dialogY = (screenSize.Height - DialogHeight) / 2;
        var bounds  = new Rect(dialogX, dialogY, DialogWidth, DialogHeight);

        int leftIdx  = Array.IndexOf(ViewModes,    leftMode);
        int rightIdx = Array.IndexOf(ViewModes,    rightMode);
        int palIdx   = FindPaletteIndex(paletteName);
        if (leftIdx  < 0) leftIdx  = 0;
        if (rightIdx < 0) rightIdx = 0;
        if (palIdx   < 0) palIdx   = 0;

        int focusRow = 0; // 0=left, 1=right, 2=palette, 3=file highlighting

        Draw(bounds, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);

        while (true)
        {
            var key = _screen.ReadKey();

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;

                case ConsoleKey.F10:
                    return new SettingsDialogResult(
                        ViewModes[leftIdx],
                        ViewModes[rightIdx],
                        PaletteNames[palIdx],
                        hlEnabled);

                case ConsoleKey.UpArrow:
                    focusRow = (focusRow + 3) % 4;
                    Draw(bounds, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                    break;

                case ConsoleKey.DownArrow:
                    focusRow = (focusRow + 1) % 4;
                    Draw(bounds, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                    break;

                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    Cycle(focusRow, ref leftIdx, ref rightIdx, ref palIdx, ref hlEnabled);
                    Draw(bounds, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                    break;
            }
        }
    }

    private static void Cycle(int focusRow, ref int leftIdx, ref int rightIdx, ref int palIdx, ref bool hlEnabled)
    {
        switch (focusRow)
        {
            case 0: leftIdx  = (leftIdx  + 1) % ViewModes.Length;    break;
            case 1: rightIdx = (rightIdx + 1) % ViewModes.Length;    break;
            case 2: palIdx   = (palIdx   + 1) % PaletteNames.Length; break;
            case 3: hlEnabled = !hlEnabled;                           break;
        }
    }

    // ── drawing ───────────────────────────────────────────────────────────────

    private void Draw(
        Rect   bounds,
        int    focusRow,
        int    leftIdx,
        int    rightIdx,
        int    palIdx,
        bool   hlEnabled)
    {
        using var frame = _screen.BeginFrame();

        var palette = PaletteRegistry.Resolve(PaletteNames[palIdx]);
        var fill    = new CellStyle(palette.NormalFileFg,         palette.PanelBackground);
        var border  = new CellStyle(palette.PanelBorderActiveFg,  palette.PanelBackground);
        var title   = new CellStyle(palette.PanelTitleActiveFg,   palette.PanelTitleActiveBg);
        var focused = new CellStyle(palette.SelectedFg,           palette.SelectedBg);

        _screen.FillRegion(bounds, fill);
        _screen.DrawDoubleBox(bounds, border);

        const string titleText = " Settings ";
        int titleX = bounds.X + (bounds.Width - titleText.Length) / 2;
        _screen.Write(titleX, bounds.Y, titleText, title);

        int contentX = bounds.X + 3;
        int valueX   = bounds.X + 18;
        int valueW   = bounds.Width - 19;

        DrawSettingRow(contentX, valueX, bounds.Y + 2, valueW,
            "Left panel:", ViewModeLabel(ViewModes[leftIdx]),
            focusRow == 0, fill, focused);

        DrawSettingRow(contentX, valueX, bounds.Y + 3, valueW,
            "Right panel:", ViewModeLabel(ViewModes[rightIdx]),
            focusRow == 1, fill, focused);

        DrawSettingRow(contentX, valueX, bounds.Y + 4, valueW,
            "Palette:", PaletteNames[palIdx],
            focusRow == 2, fill, focused);

        DrawSettingRow(contentX, valueX, bounds.Y + 5, valueW,
            "File highlight:", hlEnabled ? "Enabled" : "Disabled",
            focusRow == 3, fill, focused);

        _screen.Write(contentX, bounds.Y + 7,  "Enter/Space  change value", fill);
        _screen.Write(contentX, bounds.Y + 8,  "Up/Down      select item",  fill);
        _screen.Write(contentX, bounds.Y + 9,  "F10          save & close", fill);
        _screen.Write(contentX, bounds.Y + 10, "Esc          close",        fill);
    }

    private void DrawSettingRow(
        int labelX, int valueX, int y, int valueW,
        string label, string value,
        bool isFocused, CellStyle normalStyle, CellStyle focusedStyle)
    {
        var style = isFocused ? focusedStyle : normalStyle;
        int labelW = Math.Max(0, valueX - labelX);
        _screen.Write(labelX, y, label.PadRight(labelW), style);
        string val = value.Length > valueW ? value[..valueW] : value.PadRight(valueW);
        _screen.Write(valueX, y, val, style);
    }

    private static string ViewModeLabel(PanelViewMode mode) => mode switch
    {
        PanelViewMode.BriefTwoColumns => "Brief two columns",
        _                             => "Full",
    };

    private static int FindPaletteIndex(string paletteName) =>
        Array.FindIndex(PaletteNames,
            name => string.Equals(name, paletteName, StringComparison.OrdinalIgnoreCase));
}
