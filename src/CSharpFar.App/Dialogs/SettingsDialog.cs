using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
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
    private const int BodyRowCount = 9;

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
        int dialogWidth = Math.Min(DialogWidth, Math.Max(20, screenSize.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(6, screenSize.Height - 2));
        int dialogX = Math.Max(0, (screenSize.Width  - dialogWidth)  / 2);
        int dialogY = Math.Max(0, (screenSize.Height - dialogHeight) / 2);
        var bounds  = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        int leftIdx  = Array.IndexOf(ViewModes,    leftMode);
        int rightIdx = Array.IndexOf(ViewModes,    rightMode);
        int palIdx   = FindPaletteIndex(paletteName);
        if (leftIdx  < 0) leftIdx  = 0;
        if (rightIdx < 0) rightIdx = 0;
        if (palIdx   < 0) palIdx   = 0;

        int focusRow = 0; // 0=left, 1=right, 2=palette, 3=file highlighting
        int bodyScrollTop = NormalizeBodyScroll(bounds, focusRow, 0);
        ScrollBarDragState? bodyScrollbarDrag = null;

        Draw(bounds, bodyScrollTop, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);

        while (true)
        {
            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent mouse &&
                TryHandleBodyScrollbarMouse(mouse, bounds, ref bodyScrollTop, ref bodyScrollbarDrag))
            {
                Draw(bounds, bodyScrollTop, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

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
                    bodyScrollTop = NormalizeBodyScroll(bounds, focusRow, bodyScrollTop);
                    Draw(bounds, bodyScrollTop, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                    break;

                case ConsoleKey.DownArrow:
                    focusRow = (focusRow + 1) % 4;
                    bodyScrollTop = NormalizeBodyScroll(bounds, focusRow, bodyScrollTop);
                    Draw(bounds, bodyScrollTop, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
                    break;

                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    Cycle(focusRow, ref leftIdx, ref rightIdx, ref palIdx, ref hlEnabled);
                    Draw(bounds, bodyScrollTop, focusRow, leftIdx, rightIdx, palIdx, hlEnabled);
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
        int    bodyScrollTop,
        int    focusRow,
        int    leftIdx,
        int    rightIdx,
        int    palIdx,
        bool   hlEnabled)
    {
        using var frame = _screen.BeginFrame();

        var palette = PaletteRegistry.Resolve(PaletteNames[palIdx]);
        var fill    = new CellStyle(palette.MenuNormalFg, palette.MenuNormalBg);
        var border  = new CellStyle(palette.MenuBorderFg, palette.MenuBorderBg);
        var title   = new CellStyle(palette.MenuNormalFg, palette.MenuNormalBg);
        var focused = new CellStyle(palette.MenuActiveFg, palette.MenuActiveBg);

        var popupOptions = new PopupRenderOptions
        {
            BorderStyle = border,
            BackgroundStyle = fill,
            ShadowStyle = new CellStyle(palette.MenuShadowFg, palette.MenuShadowBg),
            TitleStyle = title,
        };

        int viewportRows = Math.Max(0, bounds.Height - 2);
        var scrollState = BodyRowCount > viewportRows
            ? new ScrollState
            {
                TotalItems = BodyRowCount,
                ViewportItems = viewportRows,
                FirstVisibleIndex = bodyScrollTop,
            }
            : null;

        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Settings", true, popupOptions, scrollState, (_, contentBounds) =>
        {
            int contentX = contentBounds.X + 2;
            int valueX   = Math.Min(contentBounds.Right, contentX + 15);
            int valueW   = Math.Max(0, contentBounds.Right - valueX);

            DrawBodySettingRow(0,
                "Left panel:", ViewModeLabel(ViewModes[leftIdx]),
                focusRow == 0, fill, focused);

            DrawBodySettingRow(1,
                "Right panel:", ViewModeLabel(ViewModes[rightIdx]),
                focusRow == 1, fill, focused);

            DrawBodySettingRow(2,
                "Palette:", PaletteNames[palIdx],
                focusRow == 2, fill, focused);

            DrawBodySettingRow(3,
                "File highlight:", hlEnabled ? "Enabled" : "Disabled",
                focusRow == 3, fill, focused);

            DrawBodyText(5, "Enter/Space  change value", fill);
            DrawBodyText(6, "Up/Down      select item", fill);
            DrawBodyText(7, "F10          save & close", fill);
            DrawBodyText(8, "Esc          close", fill);

            int? BodyY(int virtualRow)
            {
                int row = virtualRow - bodyScrollTop;
                return row >= 0 && row < contentBounds.Height ? contentBounds.Y + row : null;
            }

            void DrawBodySettingRow(
                int virtualRow,
                string label,
                string value,
                bool isFocused,
                CellStyle normalStyle,
                CellStyle focusedStyle)
            {
                if (BodyY(virtualRow) is { } y)
                    DrawSettingRow(contentX, valueX, y, valueW, label, value, isFocused, normalStyle, focusedStyle);
            }

            void DrawBodyText(int virtualRow, string text, CellStyle style)
            {
                if (BodyY(virtualRow) is not { } y)
                    return;

                int width = Math.Max(0, contentBounds.Right - contentX);
                _screen.Write(contentX, y, Truncate(text, width).PadRight(width), style);
            }
        });
    }

    private static int NormalizeBodyScroll(Rect bounds, int focusRow, int bodyScrollTop)
    {
        int viewportRows = Math.Max(1, bounds.Height - 2);
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
        bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusRow, bodyScrollTop, viewportRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
    }

    private static bool TryHandleBodyScrollbarMouse(
        MouseConsoleInputEvent mouse,
        Rect bounds,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        int viewportRows = Math.Max(1, bounds.Height - 2);
        if (BodyRowCount <= viewportRows)
            return false;

        var scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 1, 1, viewportRows);
        return ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            scrollbarBounds,
            BodyRowCount,
            viewportRows,
            ref bodyScrollTop,
            ref bodyScrollbarDrag);
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

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "~";
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
