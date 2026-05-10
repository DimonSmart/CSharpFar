using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using AppSettings = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Rendering;

internal sealed class PanelStatusRenderer
{
    private readonly ScreenRenderer _screen;

    public PanelStatusRenderer(ScreenRenderer screen) => _screen = screen;

    /// <summary>
    /// Number of status rows (separator + content) reserved at the bottom of the panel.
    /// With null/default options returns 3, matching current behaviour.
    /// </summary>
    public static int GetStatusRowCount(AppSettings.PanelOptionsSettings? options = null)
    {
        if (options != null && !options.ShowStatusLine) return 0;

        int rows = 2; // separator + current item row

        bool showTotal = options == null || options.ShowFilesTotalInformation;
        bool showFree  = options?.ShowFreeSize == true;
        if (showTotal || showFree) rows++;

        return rows;
    }

    public void Render(
        Rect                            bounds,
        FilePanelState                  state,
        CellStyle                       style,
        CellStyle                       separatorStyle,
        AppSettings.PanelOptionsSettings? options = null)
    {
        int statusRowCount = GetStatusRowCount(options);
        if (statusRowCount == 0) return;
        if (bounds.Width < 2 || bounds.Height < statusRowCount + 2) return;

        int innerWidth = bounds.Width - 2;
        int x          = bounds.X + 1;
        int separatorY = bounds.Bottom - 1 - statusRowCount;
        int itemY      = separatorY + 1;

        _screen.WriteChar(bounds.X,          separatorY, '╟', separatorStyle);
        _screen.Write(x,                     separatorY, new string('─', innerWidth), separatorStyle);
        _screen.WriteChar(bounds.Right - 1,  separatorY, '╢', separatorStyle);
        WriteRow(x, itemY, innerWidth, FormatCurrentItem(state, innerWidth), style);

        if (statusRowCount >= 3)
            WriteRow(x, itemY + 1, innerWidth, FormatStatsRow(state, options), style);
    }

    /// <summary>Y coordinate of the separator row, or -1 if status is hidden.</summary>
    internal static int SeparatorRow(Rect bounds, AppSettings.PanelOptionsSettings? options = null)
    {
        int count = GetStatusRowCount(options);
        return count > 0 ? bounds.Bottom - 1 - count : -1;
    }

    private void WriteRow(int x, int y, int width, string text, CellStyle style)
    {
        string row = Truncate(text, width).PadRight(width);
        _screen.Write(x, y, row, style);
    }

    internal static string FormatCurrentItem(FilePanelState state, int width)
    {
        if (state.CursorIndex < 0 || state.CursorIndex >= state.Items.Count)
            return string.Empty;

        var item = state.Items[state.CursorIndex];
        string kind = item.IsParentDirectory ? "Up"
                    : item.IsDirectory       ? "<DIR>"
                    : FormatSize(item.Size ?? 0);
        string stamp = FormatTimestamp(item.LastWriteTime);

        int kindWidth  = Math.Max(5, kind.Length);
        int stampWidth = stamp.Length;
        int nameWidth  = Math.Max(0, width - kindWidth - stampWidth - 2);
        string itemName = state.ShowCurrentItemFullPath ? item.FullPath : item.Name;
        string name    = Truncate(itemName, nameWidth).PadRight(nameWidth);

        return $"{name} {kind.PadLeft(kindWidth)} {stamp}";
    }

    private static string FormatStatsRow(FilePanelState state, AppSettings.PanelOptionsSettings? options)
    {
        bool showTotal = options == null || options.ShowFilesTotalInformation;
        bool showFree  = options?.ShowFreeSize == true;

        var sb = new System.Text.StringBuilder();

        if (showTotal)
        {
            var summary = state.Summary;
            if (summary != null)
            {
                sb.Append($"Bytes: {FormatSize(summary.TotalFileSize)}, files: {summary.FileCount}, folders: {summary.DirectoryCount}");
                if (summary.SelectedCount > 0)
                    sb.Append($", Selected: {summary.SelectedCount}, {FormatSize(summary.SelectedFileSize)}");
            }
            else
            {
                sb.Append(FormatDirectoryStats(state));
            }
        }

        if (showFree)
        {
            if (sb.Length > 0) sb.Append(", ");
            var space = state.Summary?.VolumeSpace;
            if (space != null)
                sb.Append($"Free: {FormatSize(space.FreeBytesAvailable)}");
            else
                sb.Append("Free: n/a");
        }

        return sb.ToString();
    }

    internal static string FormatDirectoryStats(FilePanelState state)
    {
        long bytes   = 0;
        int  files   = 0;
        int  folders = 0;

        foreach (var item in state.Items)
        {
            if (item.IsParentDirectory) continue;
            if (item.IsDirectory) folders++;
            else { files++; bytes += item.Size ?? 0; }
        }

        return $"Bytes: {FormatSize(bytes)}, files: {files}, folders: {folders}";
    }

    internal static string FormatSize(long bytes)
    {
        const double kb = 1_000d;
        const double mb = 1_000_000d;
        const double gb = 1_000_000_000d;

        return bytes switch
        {
            < 1_000L         => bytes.ToString(),
            < 1_000_000L     => FormatScaled(bytes / kb, "K"),
            < 1_000_000_000L => FormatScaled(bytes / mb, "M"),
            _                => FormatScaled(bytes / gb, "G"),
        };
    }

    private static string FormatScaled(double value, string suffix)
    {
        string number = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            .Replace('.', ',');
        return $"{number} {suffix}";
    }

    private static string FormatTimestamp(DateTime time) =>
        time == default ? string.Empty : time.ToString("dd.MM.yy HH:mm");

    internal static string Truncate(string text, int maxLen)
    {
        if (maxLen <= 0) return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }
}
