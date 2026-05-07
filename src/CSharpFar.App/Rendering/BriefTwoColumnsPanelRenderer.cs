using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

/// <summary>
/// Far Manager-style two-column brief view.
/// Items fill the first column top-to-bottom, then the second column.
/// No size column; directories are not marked with &lt;DIR&gt;.
/// </summary>
public sealed class BriefTwoColumnsPanelRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public BriefTwoColumnsPanelRenderer(ScreenRenderer screen, ConsolePalette palette)
    {
        _screen  = screen;
        _palette = palette;
    }

    /// <summary>
    /// Total number of visible items (both columns combined).
    /// Rows per column exclude top border, header row, status rows, and bottom border.
    /// </summary>
    public static int VisibleRows(Rect bounds) => 2 * Math.Max(0, bounds.Height - 3 - PanelStatusRenderer.StatusRowCount);

    public void Render(Rect bounds, FilePanelState state, bool isActive)
    {
        var p = _palette;

        var fill      = new CellStyle(isActive ? p.NormalFileFg : p.NormalFileInactiveFg, p.PanelBackground);
        var border    = new CellStyle(isActive ? p.PanelBorderActiveFg : p.PanelBorderInactiveFg, p.PanelBackground);
        var pathHdr   = new CellStyle(isActive ? p.PanelTitleActiveFg  : p.PanelTitleInactiveFg,
                                      isActive ? p.PanelTitleActiveBg  : p.PanelBackground);
        var footer    = new CellStyle(isActive ? p.FooterActiveFg : p.FooterInactiveFg, p.PanelBackground);
        var colHdr    = new CellStyle(p.ColumnHeaderFg, p.PanelBackground);
        var cursor    = new CellStyle(isActive ? p.CursorActiveFg : p.CursorInactiveFg,
                                      isActive ? p.CursorActiveBg : p.CursorInactiveBg);
        var fileStyle = fill;
        var dirStyle  = new CellStyle(isActive ? p.DirectoryFg : p.DirectoryInactiveFg, p.PanelBackground);
        var selStyle  = new CellStyle(p.SelectedFg, p.SelectedBg);

        // Fill + border
        _screen.FillRegion(bounds, fill);
        _screen.DrawDoubleBox(bounds, border);

        // Path header in top border
        int pathMaxLen = bounds.Width - 6;
        string pathLabel = TruncatePath(state.CurrentDirectory, pathMaxLen);
        _screen.Write(bounds.X + 2, bounds.Y, $" {pathLabel} ", pathHdr);

        int innerWidth  = bounds.Width - 2;
        int sepOffset   = innerWidth / 2;      // separator position within inner area
        int col1Width   = sepOffset;
        int col2Width   = innerWidth - sepOffset - 1; // -1 for the │ separator

        // ── Column header row (Y+1) ───────────────────────────────────────────
        int headerY  = bounds.Y + 1;
        string h1    = Fit("n        Name", col1Width);
        string h2    = Fit("Name",         col2Width);
        _screen.Write(bounds.X + 1,              headerY, h1, colHdr);
        _screen.WriteChar(bounds.X + 1 + sepOffset, headerY, '│', colHdr);
        _screen.Write(bounds.X + 1 + sepOffset + 1, headerY, h2, colHdr);

        // ── Content rows ──────────────────────────────────────────────────────
        int contentTop  = bounds.Y + 2;
        int rowsPerCol  = Math.Max(0, bounds.Height - 3 - PanelStatusRenderer.StatusRowCount);
        int col1X       = bounds.X + 1;
        int col2X       = bounds.X + 1 + sepOffset + 1;

        for (int row = 0; row < rowsPerCol; row++)
        {
            int y = contentTop + row;

            // Column 1
            RenderCell(state.ScrollOffset + row,              col1X, y, col1Width,
                       state, cursor, fileStyle, dirStyle, selStyle, fill);

            // Separator
            _screen.WriteChar(bounds.X + 1 + sepOffset, y, '│', border);

            // Column 2
            RenderCell(state.ScrollOffset + rowsPerCol + row, col2X, y, col2Width,
                       state, cursor, fileStyle, dirStyle, selStyle, fill);
        }

        new PanelStatusRenderer(_screen).Render(bounds, state, footer);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void RenderCell(
        int itemIdx, int x, int y, int width,
        FilePanelState state,
        CellStyle cursor, CellStyle fileStyle, CellStyle dirStyle,
        CellStyle selStyle, CellStyle fill)
    {
        if (width <= 0)
            return;

        if (itemIdx < 0 || itemIdx >= state.Items.Count)
        {
            _screen.Write(x, y, new string(' ', width), fill);
            return;
        }

        var  item       = state.Items[itemIdx];
        bool isCursor   = itemIdx == state.CursorIndex;
        bool isSelected = !item.IsParentDirectory && state.SelectedPaths.Contains(item.FullPath);

        CellStyle style = isCursor   ? cursor
                        : isSelected ? selStyle
                        : item.IsDirectory ? dirStyle
                        : fileStyle;

        string name = Fit(item.Name, width);

        _screen.Write(x, y, name, style);
    }

    private static string Fit(string text, int width) =>
        PanelStatusRenderer.Truncate(text, width).PadRight(Math.Max(0, width));

    private static string TruncatePath(string path, int maxLen)
    {
        if (maxLen <= 0) return string.Empty;
        if (path.Length <= maxLen) return path;
        return "\u2026" + path[^(maxLen - 1)..];
    }
}
