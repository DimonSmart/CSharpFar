using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

/// <summary>
/// Far-like "Change drive" modal dialog.
/// Returns the selected VolumeSelectionItem, or null if cancelled.
/// </summary>
internal sealed class DriveDialog
{
    private const int DialogWidth = 48;

    private const int DiskColW = 18;
    private const int SizeColW = 10;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DriveDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        if (items.Count == 0)
        {
            new MessageDialog(_screen, _palette).Show("Change drive", "No volumes found.");
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
        int dlgH       = visible + 6;
        int dlgX       = Math.Max(0, (size.Width  - DialogWidth) / 2);
        int dlgY       = Math.Max(0, (size.Height - dlgH)        / 2);
        var bounds     = new Rect(dlgX, dlgY, DialogWidth, dlgH);

        int cursor    = initialCursor;
        // Centre the initial cursor in the visible area.
        int scrollTop = Math.Min(
            Math.Max(0, cursor - visible / 2),
            Math.Max(0, items.Count - visible));
        ScrollBarDragState? scrollbarDrag = null;

        // Track last cycle shortcut for multi-match cycling
        string? lastShortcut = null;

        while (true)
        {
            Draw(items, bounds, cursor, scrollTop, visible);
            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent mouse &&
                TryHandleScrollbarMouse(mouse, items.Count, visible, bounds, ref cursor, ref scrollTop, ref scrollbarDrag))
            {
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

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
                        new MessageDialog(_screen, _palette).Show(
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

        _modalRenderer.Render(_screen, bounds, "Change drive", true, DriveOuterOptions, DriveFrameOptions, (_, layout) =>
        {
            Rect frameBounds = layout.FrameBounds;
            Rect contentBounds = layout.ContentBounds;
            const string hint = " Enter  Esc ";
            int hintX = frameBounds.X + (frameBounds.Width - hint.Length) / 2;
            _screen.Write(hintX, frameBounds.Y + frameBounds.Height - 1, hint, PaletteStyles.DialogTitle(_palette));

            WriteHeader(contentBounds.X, contentBounds.Y, contentBounds.Width);
            WriteTableSeparator(contentBounds.X, contentBounds.Y + 1, contentBounds.Width);

            for (int i = 0; i < visible; i++)
            {
                int idx = scrollTop + i;
                if (idx >= items.Count) break;

                WriteRow(
                    items[idx],
                    contentBounds.X,
                    contentBounds.Y + 2 + i,
                    contentBounds.Width,
                    idx == cursor);
            }

            if (items.Count > visible)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    _screen,
                    new Rect(frameBounds.Right - 1, contentBounds.Y + 2, 1, visible),
                    new ScrollState
                    {
                        TotalItems = items.Count,
                        ViewportItems = visible,
                        FirstVisibleIndex = scrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    PaletteStyles.DialogBorder(_palette));
            }
        });

        _screen.SetCursorVisible(false);
    }

    private static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        int itemCount,
        int visible,
        Rect outerBounds,
        ref int cursor,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (itemCount <= visible)
            return false;

        var frameBounds = new Rect(
            outerBounds.X + 1,
            outerBounds.Y + 1,
            Math.Max(1, outerBounds.Width - 2),
            Math.Max(1, outerBounds.Height - 2));
        var contentBounds = new Rect(
            frameBounds.X + 1,
            frameBounds.Y + 1,
            Math.Max(0, frameBounds.Width - 2),
            Math.Max(0, frameBounds.Height - 2));
        var scrollbarBounds = new Rect(frameBounds.Right - 1, contentBounds.Y + 2, 1, visible);

        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            scrollbarBounds,
            itemCount,
            visible,
            ref cursor,
            ref scrollTop,
            ref scrollbarDrag);
    }

    private void WriteHeader(int x, int y, int width)
    {
        string header =
            Fit("Disk", DiskColW) +
            " │ " +
            Fit("Free", SizeColW) +
            " │ " +
            Fit("Total", SizeColW);
        _screen.Write(x, y, TruncateToWidth(header, width).PadRight(width), PaletteStyles.DialogTitle(_palette));
    }

    private void WriteTableSeparator(int x, int y, int width)
    {
        string separator =
            new string('─', DiskColW) +
            "─┼─" +
            new string('─', SizeColW) +
            "─┼─" +
            new string('─', SizeColW);
        _screen.Write(x, y, TruncateToWidth(separator, width).PadRight(width), PaletteStyles.DialogBorder(_palette));
    }

    private void WriteRow(VolumeSelectionItem item, int x, int y, int innerWidth, bool isCursor)
    {
        if (innerWidth <= 0)
            return;

        var normalStyle = isCursor ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
        var highlightStyle = isCursor ? PaletteStyles.InputHighlight(_palette) : PaletteStyles.DialogHighlight(_palette);

        string displayName = item.Volume?.DisplayName ?? item.Label;
        string kindStr = item.Volume != null
            ? KindLabel(item.Volume.Kind, item.Volume.Status)
            : "";

        string diskCol = Fit($"{displayName} {kindStr}".Trim(), DiskColW);
        var (freeCol, totalCol) = BuildSizeCols(item.Volume);

        WriteSegment(ref x, y, ref innerWidth, diskCol, highlightStyle);
        WriteSegment(ref x, y, ref innerWidth, " │ ", normalStyle);
        WriteSegment(ref x, y, ref innerWidth, freeCol, normalStyle);
        WriteSegment(ref x, y, ref innerWidth, " │ ", normalStyle);
        WriteSegment(ref x, y, ref innerWidth, totalCol, normalStyle);

        if (innerWidth > 0)
            _screen.Write(x, y, new string(' ', innerWidth), normalStyle);
    }

    private void WriteSegment(ref int x, int y, ref int remainingWidth, string text, CellStyle style)
    {
        if (remainingWidth <= 0)
            return;

        string visible = TruncateToWidth(text, remainingWidth);
        _screen.Write(x, y, visible, style);
        x += visible.Length;
        remainingWidth -= visible.Length;
    }

    private static (string Free, string Total) BuildSizeCols(FileSystemVolume? vol)
    {
        if (vol?.Status == VolumeStatus.Ready && vol.TotalBytes.HasValue && vol.FreeBytes.HasValue)
        {
            string free = FormatBytes(vol.FreeBytes.Value).PadLeft(SizeColW);
            string total = FormatBytes(vol.TotalBytes.Value).PadLeft(SizeColW);
            return (free, total);
        }

        return (new string(' ', SizeColW), new string(' ', SizeColW));
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

    private static string TruncateToWidth(string text, int width) =>
        text.Length <= width ? text : text[..width];

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

    private PopupRenderOptions DriveOuterOptions =>
        PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false };

    private PopupRenderOptions DriveFrameOptions =>
        PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false };
}
