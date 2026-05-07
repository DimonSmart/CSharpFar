using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal sealed class PanelStatusRenderer
{
    public const int StatusRowCount = 3;

    private readonly ScreenRenderer _screen;

    public PanelStatusRenderer(ScreenRenderer screen) => _screen = screen;

    public void Render(Rect bounds, FilePanelState state, CellStyle style, CellStyle separatorStyle)
    {
        if (bounds.Width < 2 || bounds.Height < StatusRowCount + 2)
            return;

        int innerWidth = bounds.Width - 2;
        int x          = bounds.X + 1;
        int separatorY = bounds.Bottom - 4;
        int itemY      = bounds.Bottom - 3;
        int statsY     = bounds.Bottom - 2;

        _screen.WriteChar(bounds.X, separatorY, '╟', separatorStyle);
        _screen.Write(x, separatorY, new string('─', innerWidth), separatorStyle);
        _screen.WriteChar(bounds.Right - 1, separatorY, '╢', separatorStyle);
        WriteRow(x, itemY,  innerWidth, FormatCurrentItem(state, innerWidth), style);
        WriteRow(x, statsY, innerWidth, FormatDirectoryStats(state),           style);
    }

    internal static int SeparatorRow(Rect bounds) => bounds.Bottom - 4;

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

        int kindWidth = Math.Max(5, kind.Length);
        int stampWidth = stamp.Length;
        int nameWidth = Math.Max(0, width - kindWidth - stampWidth - 2);
        string name = Truncate(item.Name, nameWidth).PadRight(nameWidth);

        return $"{name} {kind.PadLeft(kindWidth)} {stamp}";
    }

    internal static string FormatDirectoryStats(FilePanelState state)
    {
        long bytes = 0;
        int files = 0;
        int folders = 0;

        foreach (var item in state.Items)
        {
            if (item.IsParentDirectory)
                continue;

            if (item.IsDirectory)
                folders++;
            else
            {
                files++;
                bytes += item.Size ?? 0;
            }
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
        if (maxLen <= 0)
            return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }
}
