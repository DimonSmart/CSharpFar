using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Far-like "Change drive" modal dialog.
/// Returns the selected VolumeSelectionItem, or null if cancelled.
/// </summary>
internal sealed class DriveDialog
{
    private const int DialogWidth = 46;

    // Inner row column widths: 1 + 8 + 1 + 12 + 1 + 9 + 2 + 9 + 1 = 44
    private const int NameColW  = 8;
    private const int KindColW  = 12;
    private const int SizeColW  = 9;

    private readonly ScreenRenderer _screen;

    public DriveDialog(ScreenRenderer screen) => _screen = screen;

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        if (items.Count == 0)
        {
            new MessageDialog(_screen).Show("Change drive", "No volumes found.");
            return null;
        }

        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            return RunLoop(items, size, Math.Clamp(initialCursor, 0, items.Count - 1));
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    private VolumeSelectionItem? RunLoop(IReadOnlyList<VolumeSelectionItem> items, ConsoleSize size, int initialCursor)
    {
        int maxVisible = Math.Max(1, size.Height - 6);
        int visible    = Math.Min(items.Count, maxVisible);
        int dlgH       = visible + 2;
        int dlgX       = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY       = Math.Max(0, (size.Height - dlgH)        / 2);
        var bounds     = new Rect(dlgX, dlgY, DialogWidth, dlgH);

        int cursor    = initialCursor;
        // Centre the initial cursor in the visible area.
        int scrollTop = Math.Min(
            Math.Max(0, cursor - visible / 2),
            Math.Max(0, items.Count - visible));

        // Track last cycle shortcut for multi-match cycling
        string? lastShortcut = null;

        while (true)
        {
            Draw(items, bounds, cursor, scrollTop, visible);
            var key = _screen.ReadKey();

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return null;

                case ConsoleKey.UpArrow:
                    if (cursor > 0)
                    {
                        cursor--;
                        if (cursor < scrollTop) scrollTop = cursor;
                    }
                    lastShortcut = null;
                    break;

                case ConsoleKey.DownArrow:
                    if (cursor < items.Count - 1)
                    {
                        cursor++;
                        if (cursor >= scrollTop + visible) scrollTop = cursor - visible + 1;
                    }
                    lastShortcut = null;
                    break;

                case ConsoleKey.PageUp:
                    cursor    = Math.Max(0, cursor - visible);
                    scrollTop = Math.Max(0, cursor);
                    lastShortcut = null;
                    break;

                case ConsoleKey.PageDown:
                    cursor    = Math.Min(items.Count - 1, cursor + visible);
                    scrollTop = Math.Max(0, cursor - visible + 1);
                    lastShortcut = null;
                    break;

                case ConsoleKey.Home:
                    cursor    = 0;
                    scrollTop = 0;
                    lastShortcut = null;
                    break;

                case ConsoleKey.End:
                    cursor    = items.Count - 1;
                    scrollTop = Math.Max(0, cursor - visible + 1);
                    lastShortcut = null;
                    break;

                case ConsoleKey.Enter:
                    var selected = items[cursor];
                    if (selected.Volume is { } vol && !IsSelectable(vol.Status))
                    {
                        string statusText = vol.Status switch
                        {
                            VolumeStatus.NotReady    => "not ready",
                            VolumeStatus.Disconnected => "disconnected",
                            _                        => "error",
                        };
                        new MessageDialog(_screen).Show(
                            "Change drive",
                            $"{vol.DisplayName}: volume is {statusText}.");
                        // Restore the drive dialog background after MessageDialog closes
                        var afterMsg = _screen.GetSize();
                        var resaved  = _screen.Capture(new Rect(0, 0, afterMsg.Width, afterMsg.Height));
                        _screen.Restore(resaved);
                        break; // stay in loop
                    }
                    return selected;

                default:
                    // Letter / digit shortcut
                    if (key.KeyChar > ' ' &&
                        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
                    {
                        string sc = key.KeyChar.ToString().ToUpperInvariant();
                        var immediate = HandleShortcut(
                            items, sc, ref cursor, ref scrollTop, visible, ref lastShortcut);
                        if (immediate is not null)
                            return immediate;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Handles a shortcut key press. Returns the item to select immediately when
    /// the shortcut is unique and the volume is available; otherwise moves the
    /// cursor and returns null.
    /// </summary>
    private static VolumeSelectionItem? HandleShortcut(
        IReadOnlyList<VolumeSelectionItem> items,
        string sc,
        ref int cursor,
        ref int scrollTop,
        int visible,
        ref string? lastShortcut)
    {
        var matches = new List<int>();
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Shortcut, sc, StringComparison.OrdinalIgnoreCase))
                matches.Add(i);
        }

        if (matches.Count == 0) return null;

        if (matches.Count == 1)
        {
            cursor    = matches[0];
            scrollTop = Math.Max(0, cursor - visible + 1);
            lastShortcut = sc;

            // Immediately select if volume is available or unchecked
            var item = items[cursor];
            if (item.Volume is null || IsSelectable(item.Volume.Status))
                return item;
            return null;
        }

        // Multiple matches: cycle to next
        int startSearch = string.Equals(lastShortcut, sc, StringComparison.OrdinalIgnoreCase)
            ? cursor + 1
            : 0;

        int next = matches.FirstOrDefault(i => i >= startSearch, matches[0]);
        cursor       = next;
        scrollTop    = Math.Max(0, cursor - visible + 1);
        lastShortcut = sc;
        return null;
    }

    // ── drawing ───────────────────────────────────────────────────────────────

    private void Draw(
        IReadOnlyList<VolumeSelectionItem> items,
        Rect  bounds,
        int   cursor,
        int   scrollTop,
        int   visible)
    {
        using var frame = _screen.BeginFrame();

        new DialogFrameRenderer().RenderFrame(_screen, bounds, "Change drive", true, Theme.DialogPopupOptions, (_, _) =>
        {
            const string hint = " Enter  Esc ";
            int hintX = bounds.X + (bounds.Width - hint.Length) / 2;
            _screen.Write(hintX, bounds.Y + bounds.Height - 1, hint, Theme.DialogTitle);

            for (int i = 0; i < visible; i++)
            {
                int idx = scrollTop + i;
                if (idx >= items.Count) break;

                string row   = FormatRow(items[idx], bounds.Width - 2);
                var    style = idx == cursor ? Theme.InputField : Theme.DialogFill;
                _screen.Write(bounds.X + 1, bounds.Y + 1 + i, row, style);
            }
        });

        _screen.SetCursorVisible(false);
    }

    private static string FormatRow(VolumeSelectionItem item, int innerWidth)
    {
        string displayName = item.Volume?.DisplayName ?? item.Label;
        string kindStr     = item.Volume != null
            ? KindLabel(item.Volume.Kind, item.Volume.Status)
            : "";

        string nameCol = Fit(displayName, NameColW);
        string kindCol = Fit(kindStr,     KindColW);
        string sizeCol = BuildSizeCol(item.Volume);

        // Layout: " " + name(8) + " " + kind(12) + " " + sizes(20) + " " = 1+8+1+12+1+20+1 = 44
        string row = $" {nameCol} {kindCol} {sizeCol} ";
        return row.Length <= innerWidth
            ? row.PadRight(innerWidth)
            : row[..innerWidth];
    }

    private static string BuildSizeCol(FileSystemVolume? vol)
    {
        // sizes section = total(9) + "  " + free(9) = 20 chars
        const int SizeTotal = SizeColW * 2 + 2; // 20
        if (vol?.Status == VolumeStatus.Ready && vol.TotalBytes.HasValue && vol.FreeBytes.HasValue)
        {
            string total = FormatBytes(vol.TotalBytes.Value).PadLeft(SizeColW);
            string free  = FormatBytes(vol.FreeBytes.Value).PadLeft(SizeColW);
            return $"{total}  {free}";
        }
        return new string(' ', SizeTotal);
    }

    internal static string KindLabel(VolumeKind kind, VolumeStatus status) =>
        status switch
        {
            VolumeStatus.NotReady    => "not ready",
            VolumeStatus.Disconnected => "disconnected",
            VolumeStatus.Error       => "error",
            _ => kind switch
            {
                VolumeKind.Fixed      => "fixed",
                VolumeKind.Removable  => "removable",
                VolumeKind.Network    => "network",
                VolumeKind.CdRom      => "cdrom",
                VolumeKind.Ram        => "ram",
                VolumeKind.MountPoint => "mount",
                VolumeKind.Pseudo     => "pseudo",
                _                    => "unknown",
            }
        };

    /// <summary>
    /// True for statuses that allow the user to select the volume.
    /// Ready and Unchecked are both selectable; NotReady/Disconnected/Error are not.
    /// </summary>
    private static bool IsSelectable(VolumeStatus status) =>
        status is VolumeStatus.Ready or VolumeStatus.Unchecked;

    /// <summary>Pads or truncates <paramref name="s"/> to exactly <paramref name="width"/> chars.</summary>
    private static string Fit(string s, int width) =>
        s.Length <= width ? s.PadRight(width) : s[..width];

    /// <summary>
    /// Formats a byte count in Far-like style: e.g. 659 G, 21,2 G, 1,86 T.
    /// Uses comma as decimal separator.
    /// </summary>
    internal static string FormatBytes(long bytes)
    {
        const long TB = 1L << 40;
        const long GB = 1L << 30;
        const long MB = 1L << 20;
        const long KB = 1L << 10;

        (double value, string unit) = bytes >= TB ? ((double)bytes / TB, "T")
            : bytes >= GB ? ((double)bytes / GB, "G")
            : bytes >= MB ? ((double)bytes / MB, "M")
            : bytes >= KB ? ((double)bytes / KB, "K")
            : (bytes,                            "B");

        string num = value >= 100 ? $"{value:F0}"
                   : value >= 10  ? $"{value:F1}"
                   :                $"{value:F2}";

        num = num.Replace('.', ',');
        return $"{num} {unit}";
    }
}
