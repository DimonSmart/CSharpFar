using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class PanelRenderer
{
    private readonly ScreenRenderer _screen;

    public PanelRenderer(ScreenRenderer screen) => _screen = screen;

    /// <summary>Number of file list rows visible inside the given bounds.</summary>
    public static int VisibleRows(Rect bounds) => Math.Max(0, bounds.Height - 2);

    public void Render(Rect bounds, FilePanelState state, bool isActive)
    {
        var border    = isActive ? Theme.PanelBorderActive    : Theme.PanelBorderInactive;
        var fill      = isActive ? Theme.PanelFillActive      : Theme.PanelFillInactive;
        var fileStyle = isActive ? Theme.FileActive           : Theme.FileInactive;
        var dirStyle  = isActive ? Theme.DirectoryActive      : Theme.DirectoryInactive;
        var cursor    = isActive ? Theme.CursorActive         : Theme.CursorInactive;
        var pathHdr   = isActive ? Theme.PathHeaderActive     : Theme.PathHeaderInactive;
        var footer    = isActive ? Theme.FooterActive         : Theme.FooterInactive;

        // Fill background + draw border
        _screen.FillRegion(bounds, fill);
        _screen.DrawBox(bounds, border);

        // Path in top border: ┌─ C:\path ──────────┐
        int pathMaxLen = bounds.Width - 6; // 6 = ┌─ + space + space + ─┐ margins
        string pathLabel = TruncatePath(state.CurrentDirectory, pathMaxLen);
        _screen.Write(bounds.X + 2, bounds.Y, $" {pathLabel} ", pathHdr);

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

            var item      = state.Items[itemIdx];
            bool isCursor = itemIdx == state.CursorIndex;
            bool isDir    = item.IsDirectory;

            CellStyle style = isCursor ? cursor : (isDir ? dirStyle : fileStyle);

            string line = FormatItem(item, nameCol, sizeCol);
            _screen.Write(bounds.X + 1, y, line, style);
        }

        // Footer in bottom border: └─ N items ──────────┘
        string footerText = $" {state.Items.Count} items ";
        if (footerText.Length <= bounds.Width - 4)
            _screen.Write(bounds.X + 2, bounds.Bottom - 1, footerText, footer);
    }

    private static string FormatItem(FilePanelItem item, int nameWidth, int sizeWidth)
    {
        string name;
        if (item.Name.Length <= nameWidth)
            name = item.Name.PadRight(nameWidth);
        else
            name = item.Name[..(nameWidth - 1)] + "~";

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

    private static string TruncatePath(string path, int maxLen)
    {
        if (maxLen <= 0) return string.Empty;
        if (path.Length <= maxLen) return path;
        return "\u2026" + path[^(maxLen - 1)..]; // …path_tail
    }
}
