using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using AppSettings = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class PanelRenderer
{
    private readonly ScreenRenderer                      _screen;
    private readonly ConsolePalette                      _palette;
    private readonly IFileHighlightService?              _highlight;
    private readonly AppSettings.PanelOptionsSettings?   _options;

    public PanelRenderer(
        ScreenRenderer                     screen,
        ConsolePalette?                    palette   = null,
        IFileHighlightService?             highlight = null,
        AppSettings.PanelOptionsSettings?  options   = null)
    {
        _screen    = screen;
        _palette   = palette ?? PaletteRegistry.Default;
        _highlight = highlight;
        _options   = options;
    }

    /// <summary>Number of file list rows visible inside the given bounds (Full mode).</summary>
    public static int VisibleRows(Rect bounds, AppSettings.PanelOptionsSettings? options = null) =>
        Math.Max(0, bounds.Height - 2 - PanelStatusRenderer.GetStatusRowCount(options));

    public void Render(Rect bounds, FilePanelState state, bool isActive,
                       PanelViewMode mode = PanelViewMode.Full)
    {
        if (mode == PanelViewMode.BriefTwoColumns)
        {
            new BriefTwoColumnsPanelRenderer(_screen, _palette, _highlight, _options)
                .Render(bounds, state, isActive);
            return;
        }

        RenderFull(bounds, state, isActive);
    }

    // ── Full mode ─────────────────────────────────────────────────────────────

    private void RenderFull(Rect bounds, FilePanelState state, bool isActive)
    {
        var p = _palette;

        var border    = new CellStyle(p.PanelBorderActiveFg, p.PanelBackground);
        var fill      = new CellStyle(p.NormalFileFg, p.PanelBackground);
        var fileStyle = fill;
        var dirStyle  = new CellStyle(p.DirectoryFg, p.PanelBackground);
        var cursor    = new CellStyle(p.CursorActiveFg, p.CursorActiveBg);
        var footer    = new CellStyle(p.FooterActiveFg, p.PanelBackground);
        var selStyle  = new CellStyle(p.SelectedFg, p.SelectedBg);

        // Fill background + draw border
        _screen.FillRegion(bounds, fill);
        _screen.DrawDoubleBox(bounds, border);

        // Sort mode letter in top border (before title)
        bool showSortLetter = _options == null || _options.ShowSortModeLetter;
        if (showSortLetter && bounds.Width > 2)
            _screen.WriteChar(bounds.X + 1, bounds.Y, SortModeIndicator.For(state), border);

        PanelTitleRenderer.Render(_screen, bounds, state, isActive, p);

        // File list
        int innerWidth = bounds.Width - 2;
        int listTop    = bounds.Y + 1;
        int visRows    = VisibleRows(bounds, _options);
        bool showScrollbar = _options?.ShowScrollbar == true;
        int scrollbarWidth = showScrollbar ? 1 : 0;
        int listWidth = Math.Max(0, innerWidth - scrollbarWidth);
        int sizeCol    = Math.Min(8, listWidth / 3);
        int nameCol    = Math.Max(0, listWidth - sizeCol - (sizeCol > 0 ? 1 : 0));

        for (int row = 0; row < visRows; row++)
        {
            int itemIdx = state.ScrollOffset + row;
            int y       = listTop + row;

            if (itemIdx >= state.Items.Count)
            {
                _screen.Write(bounds.X + 1, y, new string(' ', listWidth), fill);
                continue;
            }

            var  item       = state.Items[itemIdx];
            bool isCursor   = isActive && itemIdx == state.CursorIndex;
            bool isSelected = !item.IsParentDirectory &&
                              state.SelectedPaths.Contains(item.FullPath);

            CellStyle style = isCursor   ? cursor
                            : isSelected ? selStyle
                            : item.IsDirectory ? dirStyle
                            :                    fileStyle;

            var rowState = isCursor && isSelected ? FileRowState.SelectedCursor
                         : isCursor               ? FileRowState.Cursor
                         : isSelected             ? FileRowState.Selected
                         :                          FileRowState.Normal;

            string namePart = FormatName(item, nameCol);
            string sizePart = sizeCol > 0 ? " " + FormatSizePart(item, sizeCol) : string.Empty;

            CellStyle nameStyle = ApplyHighlight(style, item, rowState);

            _screen.Write(bounds.X + 1,           y, namePart, nameStyle);
            if (sizePart.Length > 0)
                _screen.Write(bounds.X + 1 + nameCol, y, sizePart, style);
        }

        if (showScrollbar && visRows > 0)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                _screen,
                new Rect(bounds.Right - 2, listTop, 1, visRows),
                new ScrollState
                {
                    TotalItems = state.Items.Count,
                    ViewportItems = visRows,
                    FirstVisibleIndex = state.ScrollOffset,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                border);
        }

        new PanelStatusRenderer(_screen).Render(bounds, state, footer, border, _options);
    }

    // ── static helpers ────────────────────────────────────────────────────────

    private CellStyle ApplyHighlight(CellStyle baseStyle, FilePanelItem item, FileRowState rowState)
    {
        if (_highlight == null) return baseStyle;
        var result = _highlight.GetHighlight(item, rowState);
        if (result.ColorOverride == null) return baseStyle;

        int fg = result.ColorOverride.Foreground ?? (int)baseStyle.Foreground;
        int bg = result.ColorOverride.Background ?? (int)baseStyle.Background;
        return new CellStyle((ConsoleColor)fg, (ConsoleColor)bg);
    }

    private static string FormatName(FilePanelItem item, int nameWidth)
    {
        if (nameWidth <= 0) return string.Empty;
        return item.Name.Length <= nameWidth
            ? item.Name.PadRight(nameWidth)
            : item.Name[..Math.Max(0, nameWidth - 1)] + "~";
    }

    private static string FormatSizePart(FilePanelItem item, int sizeWidth)
    {
        if (item.IsParentDirectory) return new string(' ', sizeWidth);
        if (item.IsDirectory)       return "<DIR>".PadLeft(sizeWidth);
        return FormatSize(item.Size ?? 0).PadLeft(sizeWidth);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_000L         => bytes.ToString(),
        < 1_000_000L     => $"{bytes / 1_000}K",
        < 1_000_000_000L => $"{bytes / 1_000_000}M",
        _                => $"{bytes / 1_000_000_000}G",
    };
}
