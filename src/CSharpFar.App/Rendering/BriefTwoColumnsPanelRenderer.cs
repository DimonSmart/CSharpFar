using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Highlighting;
using CSharpFar.Core.Models;
using AppSettings = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

/// <summary>
/// Far Manager-style two-column brief view.
/// Items fill the first column top-to-bottom, then the second column.
/// No size column; directories are not marked with &lt;DIR&gt;.
/// File name cell colors are overridden by the highlight service; the separator is not.
/// </summary>
public sealed class BriefTwoColumnsPanelRenderer
{
    private readonly ScreenRenderer                     _screen;
    private readonly ConsolePalette                     _palette;
    private readonly IFileHighlightService?             _highlight;
    private readonly AppSettings.PanelOptionsSettings?  _options;

    public BriefTwoColumnsPanelRenderer(
        ScreenRenderer                     screen,
        ConsolePalette                     palette,
        IFileHighlightService?             highlight = null,
        AppSettings.PanelOptionsSettings?  options   = null)
    {
        _screen    = screen;
        _palette   = palette;
        _highlight = highlight;
        _options   = options;
    }

    /// <summary>
    /// Total number of visible items (both columns combined).
    /// </summary>
    public static int VisibleRows(Rect bounds, AppSettings.PanelOptionsSettings? options = null) =>
        2 * RowsPerColumn(bounds, options);

    /// <summary>Number of visible item rows in one visual column.</summary>
    public static int RowsPerColumn(Rect bounds, AppSettings.PanelOptionsSettings? options = null) =>
        Math.Max(0, bounds.Height - 3 - PanelStatusRenderer.GetStatusRowCount(options));

    internal ApplicationPanelFrame Render(Rect bounds, FilePanelState state, bool isActive, PanelSide side)
    {
        var p = _palette;

        var fill      = new CellStyle(p.NormalFileFg, p.PanelBackground);
        var border    = new CellStyle(p.PanelBorderActiveFg, p.PanelBackground);
        var footer    = new CellStyle(p.FooterActiveFg, p.PanelBackground);
        var colHdr    = new CellStyle(p.ColumnHeaderFg, p.PanelBackground);
        var cursor    = new CellStyle(p.CursorActiveFg, p.CursorActiveBg);
        var fileStyle = fill;
        var dirStyle  = new CellStyle(p.DirectoryFg, p.PanelBackground);
        var selStyle  = new CellStyle(p.SelectedFg, p.SelectedBg);

        // Fill + border
        _screen.FillRegion(bounds, fill);
        _screen.DrawDoubleBox(bounds, border);

        PanelTitleRenderer.Render(_screen, bounds, state, isActive, p);

        if (state.LoadError is not null)
        {
            PanelErrorRenderer.Render(_screen, bounds, state, PanelViewMode.BriefTwoColumns, p, _options);
            new PanelStatusRenderer(_screen).Render(bounds, state, footer, border, _options);
            RenderStatusSeparatorJoin(bounds, Math.Max(0, (bounds.Width - 2) / 2), border);
            Rect? retry = PanelErrorRenderer.TryGetRetryBounds(
                    bounds,
                    state,
                    PanelViewMode.BriefTwoColumns,
                    _options,
                    out var retryBounds)
                ? retryBounds
                : null;
            return new ApplicationPanelFrame(
                side,
                bounds,
                VisibleRows(bounds, _options),
                [],
                retry,
                null);
        }

        int innerWidth  = Math.Max(0, bounds.Width - 2);
        int sepOffset   = innerWidth / 2;
        int col1Width   = sepOffset;
        int col2Width   = innerWidth - sepOffset - 1; // -1 for │

        // ── Column header row (Y+1) ───────────────────────────────────────────
        bool showSortLetter = _options == null || _options.ShowSortModeLetter;
        char? indicator = showSortLetter ? SortModeIndicator.For(state) : null;

        int    headerY = bounds.Y + 1;
        string h1      = FormatNameHeader(col1Width, indicator);
        string h2      = FormatNameHeader(col2Width, null);
        _screen.Write(bounds.X + 1,                  headerY, h1, colHdr);
        _screen.WriteChar(bounds.X + 1 + sepOffset,  headerY, '│', colHdr);
        _screen.Write(bounds.X + 1 + sepOffset + 1,  headerY, h2, colHdr);

        // ── Content rows ──────────────────────────────────────────────────────
        int contentTop = bounds.Y + 2;
        int rowsPerCol = RowsPerColumn(bounds, _options);
        int col1X      = bounds.X + 1;
        int col2X      = bounds.X + 1 + sepOffset + 1;
        var hits = new List<ApplicationPanelItemHit>();

        for (int row = 0; row < rowsPerCol; row++)
        {
            int y = contentTop + row;

            RenderCell(state.ScrollOffset + row,              col1X, y, col1Width,
                       state, isActive, cursor, fileStyle, dirStyle, selStyle, fill);
            AddHit(hits, state, state.ScrollOffset + row, col1X, y, col1Width);
            if (innerWidth > 0)
                _screen.WriteChar(bounds.X + 1 + sepOffset, y, '│', border);
            RenderCell(state.ScrollOffset + rowsPerCol + row, col2X, y, col2Width,
                       state, isActive, cursor, fileStyle, dirStyle, selStyle, fill);
            AddHit(hits, state, state.ScrollOffset + rowsPerCol + row, col2X, y, col2Width);
        }

        int visibleItems = VisibleRows(bounds, _options);
        ApplicationScrollBarFrame? scrollBar = null;
        if (rowsPerCol > 0 && state.Items.Count > visibleItems)
        {
            var scrollbarBounds = new Rect(bounds.Right - 1, contentTop, 1, rowsPerCol);
            new ScrollBarRenderer().RenderVerticalScrollbar(
                _screen,
                scrollbarBounds,
                new ScrollState
                {
                    TotalItems = state.Items.Count,
                    ViewportItems = visibleItems,
                    FirstVisibleIndex = state.ScrollOffset,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                border);
            scrollBar = new ApplicationScrollBarFrame(
                scrollbarBounds,
                state.Items.Count,
                visibleItems,
                state.ScrollOffset);
        }

        new PanelStatusRenderer(_screen).Render(bounds, state, footer, border, _options);
        RenderStatusSeparatorJoin(bounds, sepOffset, border);
        return new ApplicationPanelFrame(side, bounds, visibleItems, hits, null, scrollBar);
    }

    public void Render(Rect bounds, FilePanelState state, bool isActive) =>
        _ = Render(bounds, state, isActive, PanelSide.Left);

    // ── helpers ───────────────────────────────────────────────────────────────

    private void RenderCell(
        int itemIdx, int x, int y, int width,
        FilePanelState state,
        bool isActive,
        CellStyle cursor, CellStyle fileStyle, CellStyle dirStyle,
        CellStyle selStyle, CellStyle fill)
    {
        if (width <= 0) return;

        if (itemIdx < 0 || itemIdx >= state.Items.Count)
        {
            _screen.Write(x, y, new string(' ', width), fill);
            return;
        }

        var  item       = state.Items[itemIdx];
        bool isCursor   = isActive && itemIdx == state.CursorIndex;
        bool isSelected = !item.IsParentDirectory && state.SelectedPaths.Contains(item.FullPath);

        CellStyle style = isCursor         ? cursor
                        : isSelected       ? selStyle
                        : item.IsDirectory ? dirStyle
                        :                    fileStyle;

        var rowState = isCursor && isSelected ? FileRowState.SelectedCursor
                     : isCursor               ? FileRowState.Cursor
                     : isSelected             ? FileRowState.Selected
                     :                          FileRowState.Normal;

        string name = Fit(item.Name, width);
        CellStyle nameStyle = ApplyHighlight(style, item, rowState);
        _screen.Write(x, y, name, nameStyle);
    }

    private static void AddHit(
        List<ApplicationPanelItemHit> hits,
        FilePanelState state,
        int itemIdx,
        int x,
        int y,
        int width)
    {
        if (width <= 0 || itemIdx < 0 || itemIdx >= state.Items.Count)
            return;

        hits.Add(new ApplicationPanelItemHit(
            new Rect(x, y, width, 1),
            itemIdx,
            state.Items[itemIdx].FullPath));
    }

    private CellStyle ApplyHighlight(CellStyle baseStyle, FilePanelItem item, FileRowState rowState)
    {
        if (_highlight == null) return baseStyle;
        var result = _highlight.GetHighlight(item, rowState);
        if (result.ColorOverride == null) return baseStyle;

        int fg = result.ColorOverride.Foreground ?? (int)baseStyle.Foreground;
        int bg = result.ColorOverride.Background ?? (int)baseStyle.Background;
        return new CellStyle((ConsoleColor)fg, (ConsoleColor)bg);
    }

    private static string Fit(string text, int width) =>
        PanelStatusRenderer.Truncate(text, width).PadRight(Math.Max(0, width));

    private static string FormatNameHeader(int width, char? sortIndicator)
    {
        if (width <= 0) return string.Empty;

        var chars = Enumerable.Repeat(' ', width).ToArray();
        string name = PanelStatusRenderer.Truncate("Name", width);
        int nameX = Math.Max(0, (width - name.Length) / 2);
        name.CopyTo(0, chars, nameX, name.Length);

        if (sortIndicator.HasValue)
            chars[0] = sortIndicator.Value;

        return new string(chars);
    }

    private void RenderStatusSeparatorJoin(Rect bounds, int separatorOffset, CellStyle style)
    {
        int separatorX = bounds.X + 1 + separatorOffset;
        int separatorY = PanelStatusRenderer.SeparatorRow(bounds, _options);
        if (separatorY < 0) return;  // status hidden
        if (separatorX <= bounds.X || separatorX >= bounds.Right - 1) return;
        if (separatorY <= bounds.Y || separatorY >= bounds.Bottom - 1) return;

        _screen.WriteChar(separatorX, separatorY, '┴', style);
    }
}
