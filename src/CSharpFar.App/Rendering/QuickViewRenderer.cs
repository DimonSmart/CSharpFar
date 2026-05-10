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
internal sealed class QuickViewRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly CellStyle      _fillStyle;
    private readonly CellStyle      _borderStyle;
    private readonly CellStyle      _titleStyle;
    private readonly CellStyle      _errorStyle;

    public QuickViewRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        palette ??= PaletteRegistry.Default;

        _fillStyle   = new CellStyle(palette.NormalFileFg,        palette.PanelBackground);
        _borderStyle = new CellStyle(palette.PanelBorderActiveFg, palette.PanelBackground);
        _titleStyle  = new CellStyle(palette.PanelTitleFocusedFg,  palette.PanelBackground);
        _errorStyle  = new CellStyle(ConsoleColor.DarkYellow,     palette.PanelBackground);
    }

    public void Render(Rect bounds, FilePanelItem? item, DirectorySizeState? dirSizeState = null)
    {
        _screen.FillRegion(bounds, _fillStyle);
        _screen.DrawDoubleBox(bounds, _borderStyle);

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
            RenderDirectory(item, bounds.X + 1, contentTop, innerWidth, visRows, dirSizeState);
        else
            RenderFile(item, bounds.X + 1, contentTop, innerWidth, visRows);
    }

    // ── directory ─────────────────────────────────────────────────────────────

    private void RenderDirectory(FilePanelItem item, int x, int y, int w, int visRows, DirectorySizeState? dirSizeState)
    {
        var (rows, errorRows) = BuildDirectoryRows(item, w, dirSizeState);
        int normalCount = Math.Min(visRows, rows.Count);
        for (int i = 0; i < normalCount; i++)
            WriteRow(x, y + i, rows[i], w);
        for (int i = normalCount; i < visRows; i++)
        {
            int errIdx = i - normalCount;
            string errText = errIdx < errorRows.Count ? errorRows[errIdx] : string.Empty;
            WriteErrorRow(x, y + i, errText, w);
        }
    }

    private static (List<string> Rows, List<string> ErrorRows) BuildDirectoryRows(
        FilePanelItem item, int w, DirectorySizeState? dirSizeState)
    {
        var rows = new List<string>();

        DirectoryInfo? di = null;
        try { di = new DirectoryInfo(item.FullPath); } catch { }

        rows.Add(TruncatePath(item.FullPath, w));
        rows.Add(string.Empty);

        if (di != null && di.Exists)
        {
            rows.Add(Label("Created", di.CreationTime.ToString("yyyy-MM-dd  HH:mm:ss"), w));
            rows.Add(Label("Modified", di.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"), w));
            rows.Add(Label("Accessed", di.LastAccessTime.ToString("yyyy-MM-dd  HH:mm:ss"), w));
            rows.Add(string.Empty);

            int files = 0, dirs = 0;
            try
            {
                files = Directory.GetFiles(item.FullPath).Length;
                dirs  = Directory.GetDirectories(item.FullPath).Length;
            }
            catch { }

            rows.Add(Label("Files", files.ToString(), w));
            rows.Add(Label("Directories", dirs.ToString(), w));
            rows.Add(string.Empty);

            string sizeText = dirSizeState is null
                ? "Calculating\u2026"
                : dirSizeState.IsCompleted
                    ? FormatSize(dirSizeState.Size)
                    : FormatSize(dirSizeState.Size) + "  (scanning\u2026)";
            rows.Add(Label("Total size", sizeText, w));

            string attrs = FormatAttributes(di.Attributes);
            if (attrs.Length > 0)
            {
                rows.Add(string.Empty);
                rows.Add(Label("Attributes", attrs, w));
            }
        }
        else
        {
            rows.Add("Access denied");
        }

        var errorRows = new List<string>();
        if (dirSizeState?.Errors is { Count: > 0 } errs)
        {
            rows.Add(string.Empty);
            rows.Add(Label("Errors", errs.Count.ToString(), w));
            foreach (string err in errs)
                errorRows.Add(TruncatePath(err, w));
        }

        return (rows, errorRows);
    }

    private static string Label(string name, string value, int w)
    {
        string label = name + ":";
        int valueStart = Math.Min(14, w / 2);
        if (label.Length < valueStart)
            label = label.PadRight(valueStart);
        string full = label + value;
        return full.Length <= w ? full : full[..Math.Max(0, w - 1)] + "\u2026";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB  ({bytes:N0} bytes)";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB  ({bytes:N0} bytes)";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB  ({bytes:N0} bytes)";
    }

    private static string FormatAttributes(FileAttributes attrs)
    {
        var parts = new List<string>();
        if ((attrs & FileAttributes.ReadOnly) != 0)  parts.Add("ReadOnly");
        if ((attrs & FileAttributes.Hidden) != 0)    parts.Add("Hidden");
        if ((attrs & FileAttributes.System) != 0)    parts.Add("System");
        if ((attrs & FileAttributes.Archive) != 0)   parts.Add("Archive");
        if ((attrs & FileAttributes.Compressed) != 0) parts.Add("Compressed");
        if ((attrs & FileAttributes.Encrypted) != 0) parts.Add("Encrypted");
        return string.Join(", ", parts);
    }

    // ── file ──────────────────────────────────────────────────────────────────

    private void RenderFile(FilePanelItem item, int x, int y, int w, int visRows)
    {
        long size;
        try   { size = new FileInfo(item.FullPath).Length; }
        catch { size = 0; }

        bool isText = TextFileInfo.IsTextFile(item.FullPath);

        // Reserve bottom section (separator + info rows) for text files
        const int infoRows = 2; // BOM/line-ending row + (app row on the separator line itself)
        int textRows = (isText && visRows > infoRows + 1) ? visRows - infoRows - 1 : visRows;

        if (size > TextFileReader.MaxFileSizeBytes)
        {
            WriteRow(x, y, "File is too large to preview", w);
        }
        else
        {
            string[] lines;
            try   { lines = TextFileReader.ReadLines(item.FullPath); }
            catch { lines = ["Cannot read file"]; }

            for (int i = 0; i < textRows; i++)
            {
                string text = i < lines.Length ? RenderableLine(lines[i], w) : string.Empty;
                WriteRow(x, y + i, text, w);
            }
        }

        if (!isText || visRows <= infoRows + 1) return;

        TextFileInfo info;
        try   { info = TextFileInfo.Read(item.FullPath); }
        catch { return; }

        // Separator with associated app name centred on it
        int sepY = y + textRows;
        DrawFileSeparator(x - 1, sepY, w + 2, info.AppName);

        // Info rows below the separator
        int infoY = sepY + 1;
        string bomText   = $"BOM: {(info.BomName is not null ? info.BomName : "No")}";
        string leText    = info.LineEnding.Length > 0 ? $"  EOL: {info.LineEnding}" : string.Empty;
        WriteRow(x, infoY, (bomText + leText).PadRight(w), w);

        // Second info row — fill empty to clear
        if (infoRows > 1 && infoY + 1 < y + visRows)
            WriteRow(x, infoY + 1, string.Empty, w);
    }

    private void DrawFileSeparator(int bx, int y, int totalWidth, string? label)
    {
        // ╟──────[ App Name ]──────╢  (or just ╟────────────────────╢)
        string inner;
        if (label is { Length: > 0 } && totalWidth > 6)
        {
            string tag = $"[ {label} ]";
            int lineLen = Math.Max(0, totalWidth - 2 - tag.Length);
            int left    = lineLen / 2;
            int right   = lineLen - left;
            inner = new string('─', left) + tag + new string('─', right);
        }
        else
        {
            inner = new string('─', Math.Max(0, totalWidth - 2));
        }

        _screen.WriteChar(bx, y, '╟', _borderStyle);
        _screen.Write(bx + 1, y, inner, _borderStyle);
        _screen.WriteChar(bx + totalWidth - 1, y, '╢', _borderStyle);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void WriteRow(int x, int y, string text, int width)
    {
        if (width <= 0)
            return;

        string padded = text.Length >= width ? text[..width] : text.PadRight(width);
        _screen.Write(x, y, padded, _fillStyle);
    }

    private void WriteErrorRow(int x, int y, string text, int width)
    {
        if (width <= 0)
            return;

        string padded = text.Length >= width ? text[..width] : text.PadRight(width);
        _screen.Write(x, y, padded, _errorStyle);
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
