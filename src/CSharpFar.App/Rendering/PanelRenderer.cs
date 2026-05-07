using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class PanelRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public PanelRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen  = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    /// <summary>Number of file list rows visible inside the given bounds (Full mode).</summary>
    public static int VisibleRows(Rect bounds) => Math.Max(0, bounds.Height - 2 - PanelStatusRenderer.StatusRowCount);

    public void Render(Rect bounds, FilePanelState state, bool isActive,
                       PanelViewMode mode = PanelViewMode.Full)
    {
        if (mode == PanelViewMode.BriefTwoColumns)
        {
            new BriefTwoColumnsPanelRenderer(_screen, _palette).Render(bounds, state, isActive);
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

        PanelTitleRenderer.Render(_screen, bounds, state, isActive, p);

        // File list
        int innerWidth = bounds.Width - 2;
        int listTop    = bounds.Y + 1;
        int visRows    = VisibleRows(bounds);
        int sizeCol    = Math.Min(8, innerWidth / 3);
        int nameCol    = innerWidth - sizeCol - 1; // 1 space separator

        for (int row = 0; row < visRows; row++)
        {
            int itemIdx = state.ScrollOffset + row;
            int y       = listTop + row;

            if (itemIdx >= state.Items.Count)
            {
                _screen.Write(bounds.X + 1, y, new string(' ', innerWidth), fill);
                continue;
            }

            var item        = state.Items[itemIdx];
            bool isCursor   = isActive && itemIdx == state.CursorIndex;
            bool isDir      = item.IsDirectory;
            bool isSelected = !item.IsParentDirectory &&
                              state.SelectedPaths.Contains(item.FullPath);

            CellStyle style = isCursor   ? cursor
                            : isSelected ? selStyle
                            : isDir      ? dirStyle
                            :              fileStyle;

            string line = FormatItem(item, nameCol, sizeCol);
            _screen.Write(bounds.X + 1, y, line, style);
        }

        new PanelStatusRenderer(_screen).Render(bounds, state, footer, border);
    }

    // ── static helpers ────────────────────────────────────────────────────────

    private static string FormatItem(FilePanelItem item, int nameWidth, int sizeWidth)
    {
        string name = nameWidth <= 0 ? string.Empty
            : item.Name.Length <= nameWidth
                ? item.Name.PadRight(nameWidth)
                : item.Name[..Math.Max(0, nameWidth - 1)] + "~";

        string size;
        if (item.IsParentDirectory)
            size = new string(' ', sizeWidth);
        else if (item.IsDirectory)
            size = "<DIR>".PadLeft(sizeWidth);
        else
            size = FormatSize(item.Size ?? 0).PadLeft(sizeWidth);

        return $"{name} {size}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1_000L         => bytes.ToString(),
        < 1_000_000L     => $"{bytes / 1_000}K",
        < 1_000_000_000L => $"{bytes / 1_000_000}M",
        _                => $"{bytes / 1_000_000_000}G",
    };

}
