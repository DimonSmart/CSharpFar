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

    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DriveDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _screen = modalDialogs.Screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        if (items.Count == 0)
        {
            new MessageDialog(_modalDialogs).Show("Change drive", "No volumes found.");
            return null;
        }

        return RunLoop(items, Math.Clamp(initialCursor, 0, items.Count - 1));
    }

    private VolumeSelectionItem? RunLoop(IReadOnlyList<VolumeSelectionItem> items, int initialCursor)
    {
        int cursor    = initialCursor;
        int scrollTop = 0;
        ScrollBarDragState? scrollbarDrag = null;

        // Track last cycle shortcut for multi-match cycling
        string? lastShortcut = null;

        return _modalDialogs.Run(
            context =>
            {
                var frame = CalculateFrame(context.Size, items.Count, cursor, scrollTop);
                RenderFrame(context, items, frame);
                return frame;
            },
            (input, frame) =>
            {
            if (input is MouseConsoleInputEvent mouse &&
                TryHandleScrollbarMouse(mouse, items.Count, frame.VisibleRows, frame.ScrollbarBounds, frame.ScrollTop, ref cursor, ref scrollTop, ref scrollbarDrag))
            {
                return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;
            }

            if (input is MouseConsoleInputEvent listMouse &&
                TryHandleListMouse(listMouse, frame.ListBounds, items.Count, frame.ScrollTop, ref cursor, out bool select))
            {
                lastShortcut = null;
                if (select)
                {
                    var clicked = items[cursor];
                    if (clicked.Volume is { } clickedVol && !IsSelectable(clickedVol.Status))
                    {
                        string statusText = clickedVol.Status switch
                        {
                            VolumeStatus.NotReady     => "not ready",
                            VolumeStatus.Disconnected => "disconnected",
                            _                         => "error",
                        };
                        new MessageDialog(_modalDialogs).Show(
                            "Change drive",
                            $"{clickedVol.DisplayName}: volume is {statusText}.");
                    }
                    else
                    {
                        return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(clicked);
                    }
                }
                return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(null);

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
                        if (cursor >= scrollTop + frame.VisibleRows) scrollTop = cursor - frame.VisibleRows + 1;
                    }
                    lastShortcut = null;
                    break;

                case ConsoleKey.PageUp:
                    cursor    = Math.Max(0, cursor - frame.VisibleRows);
                    scrollTop = Math.Max(0, cursor);
                    lastShortcut = null;
                    break;

                case ConsoleKey.PageDown:
                    cursor    = Math.Min(items.Count - 1, cursor + frame.VisibleRows);
                    scrollTop = Math.Max(0, cursor - frame.VisibleRows + 1);
                    lastShortcut = null;
                    break;

                case ConsoleKey.Home:
                    cursor    = 0;
                    scrollTop = 0;
                    lastShortcut = null;
                    break;

                case ConsoleKey.End:
                    cursor    = items.Count - 1;
                    scrollTop = Math.Max(0, cursor - frame.VisibleRows + 1);
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
                        new MessageDialog(_modalDialogs).Show(
                            "Change drive",
                            $"{vol.DisplayName}: volume is {statusText}.");
                        break;
                    }
                    return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(selected);

                default:
                    // Letter / digit shortcut
                    if (key.KeyChar > ' ' &&
                        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
                    {
                        string sc = key.KeyChar.ToString().ToUpperInvariant();
                        var immediate = HandleShortcut(
                            items, sc, ref cursor, ref scrollTop, frame.VisibleRows, ref lastShortcut);
                        if (immediate is not null)
                            return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(immediate);
                    }
                    break;
            }

            return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                cursor = frame.Cursor;
                scrollTop = frame.ScrollTop;
            });
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

    private static DriveDialogFrame CalculateFrame(
        ConsoleSize size,
        int itemCount,
        int requestedCursor,
        int requestedScrollTop)
    {
        int visible = Math.Min(itemCount, Math.Max(1, size.Height - 6));
        int cursor = Math.Clamp(requestedCursor, 0, itemCount - 1);
        int scrollTop = Math.Clamp(requestedScrollTop, 0, Math.Max(0, itemCount - visible));
        if (cursor < scrollTop) scrollTop = cursor;
        if (cursor >= scrollTop + visible) scrollTop = cursor - visible + 1;
        int height = visible + 6;
        var bounds = new Rect(Math.Max(0, (size.Width - DialogWidth) / 2), Math.Max(0, (size.Height - height) / 2), Math.Min(DialogWidth, size.Width), height);
        var frameBounds = new Rect(
            bounds.X + 1,
            bounds.Y + 1,
            Math.Max(1, bounds.Width - 2),
            Math.Max(1, bounds.Height - 2));
        var contentBounds = new Rect(
            frameBounds.X + 1,
            frameBounds.Y + 1,
            Math.Max(0, frameBounds.Width - 2),
            Math.Max(0, frameBounds.Height - 2));
        var listBounds = new Rect(contentBounds.X, contentBounds.Y + 2, contentBounds.Width, visible);
        Rect? scrollbarBounds = itemCount > visible
            ? new Rect(frameBounds.Right - 1, contentBounds.Y + 2, 1, visible)
            : null;
        return new DriveDialogFrame(bounds, listBounds, scrollbarBounds, visible, cursor, scrollTop);
    }

    private void RenderFrame(
        UiRenderContext context,
        IReadOnlyList<VolumeSelectionItem> items,
        DriveDialogFrame frame)
    {
        _modalRenderer.Render(context.Screen, frame.Bounds, "Change drive", true, DriveOuterOptions, DriveFrameOptions, (_, layout) =>
        {
            Rect frameBounds = layout.FrameBounds;
            Rect contentBounds = layout.ContentBounds;
            const string hint = " Enter  Esc ";
            int hintX = frameBounds.X + (frameBounds.Width - hint.Length) / 2;
            context.Screen.Write(hintX, frameBounds.Y + frameBounds.Height - 1, hint, PaletteStyles.DialogTitle(_palette));

            WriteHeader(contentBounds.X, contentBounds.Y, contentBounds.Width);
            WriteTableSeparator(contentBounds.X, contentBounds.Y + 1, contentBounds.Width);

            for (int i = 0; i < frame.VisibleRows; i++)
            {
                int idx = frame.ScrollTop + i;
                if (idx >= items.Count) break;

                WriteRow(
                    items[idx],
                    contentBounds.X,
                    contentBounds.Y + 2 + i,
                    contentBounds.Width,
                    idx == frame.Cursor);
            }

            if (frame.ScrollbarBounds is { } scrollbarBounds)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    context.Screen,
                    scrollbarBounds,
                    new ScrollState
                    {
                        TotalItems = items.Count,
                        ViewportItems = frame.VisibleRows,
                        FirstVisibleIndex = frame.ScrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    PaletteStyles.DialogBorder(_palette));
            }
        });

        context.Screen.SetCursorVisible(false);
    }

    private readonly record struct DriveDialogFrame(
        Rect Bounds,
        Rect ListBounds,
        Rect? ScrollbarBounds,
        int VisibleRows,
        int Cursor,
        int ScrollTop);

    private static bool TryHandleListMouse(
        MouseConsoleInputEvent mouse,
        Rect listBounds,
        int itemCount,
        int scrollTop,
        ref int cursor,
        out bool select)
    {
        select = false;
        if (itemCount == 0 ||
            mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick))
        {
            return false;
        }

        if (!listBounds.Contains(mouse.X, mouse.Y))
        {
            return false;
        }

        int index = scrollTop + mouse.Y - listBounds.Y;
        if (index < 0 || index >= itemCount)
            return false;

        cursor = index;
        select = mouse.Kind == MouseEventKind.DoubleClick;
        return true;
    }

    private static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        int itemCount,
        int visible,
        Rect? scrollbarBounds,
        int committedScrollTop,
        ref int cursor,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (itemCount <= visible || scrollbarBounds is not { } bounds)
            return false;

        scrollTop = committedScrollTop;
        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            bounds,
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
            _modalDialogs.Screen.Write(x, y, TruncateToWidth(header, width).PadRight(width), PaletteStyles.DialogTitle(_palette));
    }

    private void WriteTableSeparator(int x, int y, int width)
    {
        string separator =
            new string('─', DiskColW) +
            "─┼─" +
            new string('─', SizeColW) +
            "─┼─" +
            new string('─', SizeColW);
        _modalDialogs.Screen.Write(x, y, TruncateToWidth(separator, width).PadRight(width), PaletteStyles.DialogBorder(_palette));
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
            _modalDialogs.Screen.Write(x, y, new string(' ', innerWidth), normalStyle);
    }

    private void WriteSegment(ref int x, int y, ref int remainingWidth, string text, CellStyle style)
    {
        if (remainingWidth <= 0)
            return;

        string visible = TruncateToWidth(text, remainingWidth);
        _modalDialogs.Screen.Write(x, y, visible, style);
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
