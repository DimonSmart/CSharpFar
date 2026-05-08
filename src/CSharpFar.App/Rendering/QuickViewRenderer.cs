using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

/// <summary>
/// Renders a quick-view preview of the active panel's current item
/// inside the inactive panel's bounds.
/// Files: shows text content (up to visRows lines).
/// Directories: shows path and item count.
/// </summary>
public sealed class QuickViewRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _fillStyle;
    private readonly CellStyle      _borderStyle;
    private readonly CellStyle      _titleStyle;

    public QuickViewRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        if (palette is not null)
        {
            _fillStyle   = new CellStyle(palette.NormalFileInactiveFg, palette.PanelBackground);
            _borderStyle = new CellStyle(palette.PanelBorderInactiveFg, palette.PanelBackground);
            _titleStyle  = new CellStyle(palette.PanelTitleInactiveFg,  palette.PanelBackground);
        }
        else
        {
            _fillStyle   = Theme.PanelFillInactive;
            _borderStyle = Theme.PanelBorderInactive;
            _titleStyle  = Theme.PathHeaderInactive;
        }
    }

    public void Render(Rect bounds, FilePanelItem? item)
    {
        _screen.FillRegion(bounds, _fillStyle);
        _screen.DrawBox(bounds, _borderStyle);

        const string title = " Quick View ";
        _screen.Write(bounds.X + (bounds.Width - title.Length) / 2, bounds.Y, title, _titleStyle);

        int innerWidth = bounds.Width - 2;
        int contentTop = bounds.Y + 1;
        int visRows    = Math.Max(0, bounds.Height - 2);

        if (item is null || item.IsParentDirectory)
        {
            WriteRow(bounds.X + 1, contentTop, "No file selected", innerWidth);
            return;
        }

        if (item.IsDirectory)
            RenderDirectory(item, bounds.X + 1, contentTop, innerWidth, visRows);
        else
            RenderFile(item, bounds.X + 1, contentTop, innerWidth, visRows);
    }

    // ── directory ─────────────────────────────────────────────────────────────

    private void RenderDirectory(FilePanelItem item, int x, int y, int w, int visRows)
    {
        WriteRow(x, y++, TruncatePath(item.FullPath, w), w);
        if (visRows < 2) return;

        string countText;
        try
        {
            int count = Directory.GetFileSystemEntries(item.FullPath).Length;
            countText = $"{count} item{(count == 1 ? "" : "s")}";
        }
        catch { countText = "Access denied"; }

        WriteRow(x, y, countText, w);
    }

    // ── file ──────────────────────────────────────────────────────────────────

    private void RenderFile(FilePanelItem item, int x, int y, int w, int visRows)
    {
        long size;
        try   { size = new FileInfo(item.FullPath).Length; }
        catch { size = 0; }

        if (size > TextFileReader.MaxFileSizeBytes)
        {
            WriteRow(x, y, "File is too large to preview", w);
            return;
        }

        string[] lines;
        try   { lines = TextFileReader.ReadLines(item.FullPath); }
        catch { lines = ["Cannot read file"]; }

        for (int i = 0; i < visRows; i++)
        {
            string text = i < lines.Length ? RenderableLine(lines[i], w) : string.Empty;
            WriteRow(x, y + i, text, w);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void WriteRow(int x, int y, string text, int width)
    {
        if (width <= 0)
            return;

        string padded = text.Length >= width ? text[..width] : text.PadRight(width);
        _screen.Write(x, y, padded, _fillStyle);
    }

    private static string RenderableLine(string line, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;

        line = line.Replace("\t", "    ");
        return line.Length <= maxLen ? line : line[..maxLen];
    }

    private static string TruncatePath(string path, int maxLen) =>
        maxLen <= 0 ? string.Empty :
        path.Length <= maxLen ? path :
        maxLen == 1 ? "\u2026" :
        "\u2026" + path[^(maxLen - 1)..];
}
