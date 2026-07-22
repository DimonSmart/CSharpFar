using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

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

    private static readonly UiTargetId VolumesTarget = new("drive.volumes");
    private static readonly UiTargetId VolumesScrollbarTarget = new("drive.volumes.scrollbar");

    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DriveDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        VolumeSelectionItem[] snapshot = items.ToArray();
        if (snapshot.Length == 0)
        {
            new MessageDialog(_modalDialogs).Show("Change drive", "No volumes found.");
            return null;
        }

        return RunLoop(snapshot, Math.Clamp(initialCursor, 0, snapshot.Length - 1));
    }

    private VolumeSelectionItem? RunLoop(VolumeSelectionItem[] items, int initialCursor)
    {
        var list = new ScrollableList<VolumeSelectionItem>(items, ItemText)
        {
            SelectedIndex = initialCursor,
            EmptyText = "No volumes found.",
        };
        string? lastShortcut = null;

        return _modalDialogs.RunInteractive<DriveDialogFrame, ScrollableListInputResult, VolumeSelectionItem?>(
            (context, _) =>
            {
                DriveDialogFrame frame = BuildFrame(context.Size, items, list);
                RenderFrame(context, items, frame);
                return frame;
            },
            BuildInteractionFrame,
            (input, frame, route) => RouteInput(input, frame, route, list),
            (routed, result) =>
            {
                if (routed.Input is KeyConsoleInputEvent { Key: var key })
                {
                    if (key.Key is ConsoleKey.Escape or ConsoleKey.F10)
                        return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(null);

                    if (result.Kind == ScrollableListInputResultKind.NotHandled &&
                        key.KeyChar > ' ' &&
                        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
                    {
                        string shortcut = key.KeyChar.ToString().ToUpperInvariant();
                        VolumeSelectionItem? immediate = HandleShortcut(list, shortcut, routed.Frame.ListState.ViewportRows, ref lastShortcut);
                        if (immediate is not null)
                            return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(immediate);
                    }
                }

                if (BreaksShortcutCycle(routed.Input, result))
                    lastShortcut = null;

                if (result.Kind == ScrollableListInputResultKind.Confirmed &&
                    list.SelectedItemOrDefault is { } selected)
                {
                    return TryCompleteSelection(selected);
                }

                return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;
            });
    }

    private static (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        DriveDialogFrame frame,
        UiInputRouteContext route,
        ScrollableList<VolumeSelectionItem> list)
    {
        list.ApplyCommittedFrame(frame.ListState);

        if (input is KeyConsoleInputEvent { Key: var key })
        {
            if (key.Key is ConsoleKey.Escape or ConsoleKey.F10)
                return (ScrollableListInputResult.Handled, UiInputResult.HandledResult);

            ScrollableListInputResult result = list.HandleKey(key, frame.ListState.ViewportRows);
            return (result, result.IsHandled ? UiInputResult.HandledAndInvalidate : UiInputResult.NotHandled);
        }

        if (input is MouseConsoleInputEvent mouse &&
            route.Target is UiTargetId target &&
            (target == VolumesTarget || target == VolumesScrollbarTarget))
        {
            ScrollableListInputResult result = list.HandleMouse(
                mouse,
                frame.ListBounds,
                frame.ScrollbarBounds,
                frame.ListState.ViewportRows);
            if (!result.IsHandled)
                return (result, UiInputResult.NotHandled);

            UiMouseCaptureRequest capture = result.DragStarted
                ? UiMouseCaptureRequest.Capture(VolumesScrollbarTarget, MouseButton.Left)
                : result.DragEnded ? UiMouseCaptureRequest.Release : UiMouseCaptureRequest.None;
            return (result, new UiInputResult(true, true, UiFocusRequest.None, capture));
        }

        return (ScrollableListInputResult.NotHandled, UiInputResult.NotHandled);
    }

    private ModalDialogLoopResult<VolumeSelectionItem?> TryCompleteSelection(VolumeSelectionItem selected)
    {
        if (selected.Volume is { } volume && !IsSelectable(volume.Status))
        {
            string statusText = volume.Status switch
            {
                VolumeStatus.NotReady => "not ready",
                VolumeStatus.Disconnected => "disconnected",
                _ => "error",
            };
            new MessageDialog(_modalDialogs).Show(
                "Change drive",
                $"{volume.DisplayName}: volume is {statusText}.");
            return ModalDialogLoopResult<VolumeSelectionItem?>.Continue;
        }

        return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(selected);
    }

    private static VolumeSelectionItem? HandleShortcut(
        ScrollableList<VolumeSelectionItem> list,
        string shortcut,
        int visibleRows,
        ref string? lastShortcut)
    {
        var matches = new List<int>();
        for (int index = 0; index < list.Count; index++)
        {
            if (string.Equals(list.Items[index].Shortcut, shortcut, StringComparison.OrdinalIgnoreCase))
                matches.Add(index);
        }

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
        {
            list.SelectedIndex = matches[0];
            list.EnsureSelectedVisible(visibleRows);
            lastShortcut = shortcut;

            VolumeSelectionItem item = list.Items[list.SelectedIndex];
            return item.Volume is null || IsSelectable(item.Volume.Status) ? item : null;
        }

        int startSearch = string.Equals(lastShortcut, shortcut, StringComparison.OrdinalIgnoreCase)
            ? list.SelectedIndex + 1
            : 0;
        int next = matches.FirstOrDefault(index => index >= startSearch, matches[0]);
        list.SelectedIndex = next;
        list.EnsureSelectedVisible(visibleRows);
        lastShortcut = shortcut;
        return null;
    }

    private static bool BreaksShortcutCycle(ConsoleInputEvent input, ScrollableListInputResult result)
    {
        if (!result.IsHandled)
            return false;

        if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or ConsoleKey.PageDown or ConsoleKey.Home or ConsoleKey.End })
            return true;

        return input is MouseConsoleInputEvent
        {
            Kind: MouseEventKind.Down or MouseEventKind.DoubleClick or MouseEventKind.Wheel or MouseEventKind.Move or MouseEventKind.Up,
        };
    }

    private static DriveDialogFrame BuildFrame(
        ConsoleSize size,
        VolumeSelectionItem[] items,
        ScrollableList<VolumeSelectionItem> list)
    {
        int requestedRows = Math.Min(items.Length, Math.Max(0, size.Height - 6));
        Rect bounds = new ModalDialogRenderer().CenteredOuterBounds(size, DialogWidth, requestedRows + 6);
        DriveDialogLayout layout = CalculateLayout(bounds, items.Length);
        int visibleRows = layout.ListBounds.Height;
        Rect? scrollbarBoundsCandidate = visibleRows > 0 && items.Length > visibleRows
            ? new Rect(layout.FrameBounds.Right - 1, layout.ListBounds.Y, 1, visibleRows)
            : null;
        Rect? scrollbarBounds = scrollbarBoundsCandidate is { } candidate &&
            ScrollBarInteraction.IsInteractive(
                candidate,
                new ScrollState
                {
                    TotalItems = items.Length,
                    ViewportItems = visibleRows,
                    FirstVisibleIndex = 0,
                })
            ? candidate
            : null;
        ScrollableListFrameState state = visibleRows > 0
            ? list.CalculateFrameState(visibleRows, scrollbarBounds)
            : new ScrollableListFrameState(list.SelectedIndex, list.ScrollTop, 0, null, null);
        return new DriveDialogFrame(
            items,
            bounds,
            layout.ListBounds,
            scrollbarBounds,
            state);
    }

    private static DriveDialogLayout CalculateLayout(Rect bounds, int itemCount)
    {
        if (bounds.Width < 3 || bounds.Height < 3)
            return new DriveDialogLayout(new Rect(bounds.X, bounds.Y, 0, 0), new Rect(bounds.X, bounds.Y, 0, 0));

        var frameBounds = new Rect(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
        var contentBounds = new Rect(
            frameBounds.X + 1,
            frameBounds.Y + 1,
            Math.Max(0, frameBounds.Width - 2),
            Math.Max(0, frameBounds.Height - 2));
        int visibleRows = Math.Min(itemCount, Math.Max(0, contentBounds.Height - 2));
        Rect listBounds = visibleRows > 0 && contentBounds.Width > 0
            ? new Rect(contentBounds.X, contentBounds.Y + 2, contentBounds.Width, visibleRows)
            : new Rect(contentBounds.X, contentBounds.Y, 0, 0);
        return new DriveDialogLayout(frameBounds, listBounds);
    }

    private static UiInteractionFrame BuildInteractionFrame(DriveDialogFrame frame)
    {
        var builder = new UiInteractionFrameBuilder();
        if (frame.ListBounds.Width > 0 && frame.ListBounds.Height > 0)
            builder.AddHitRegion(VolumesTarget, frame.ListBounds);
        if (frame.ScrollbarBounds is { } scrollbar)
            builder.AddHitRegion(VolumesScrollbarTarget, scrollbar);

        return frame.ListBounds.Width > 0 && frame.ListBounds.Height > 0
            ? builder
                .AddFocusEntry(VolumesTarget, 0)
                .SetDefaultFocusTarget(VolumesTarget)
                .SetKeyboardTarget(VolumesTarget)
                .Build()
            : builder.Build();
    }

    private void RenderFrame(
        UiRenderContext context,
        IReadOnlyList<VolumeSelectionItem> items,
        DriveDialogFrame frame)
    {
        _modalRenderer.Render(context.Canvas, frame.Bounds, "Change drive", true, DriveOuterOptions, DriveFrameOptions, (_, layout) =>
        {
            Rect frameBounds = layout.FrameBounds;
            Rect contentBounds = layout.ContentBounds;
            const string hint = " Enter  Esc ";
            int hintX = frameBounds.X + (frameBounds.Width - hint.Length) / 2;
            context.Canvas.Write(hintX, frameBounds.Y + frameBounds.Height - 1, hint, PaletteStyles.DialogTitle(_palette));

            WriteHeader(contentBounds.X, contentBounds.Y, contentBounds.Width);
            WriteTableSeparator(contentBounds.X, contentBounds.Y + 1, contentBounds.Width);

            for (int line = 0; line < frame.ListState.ViewportRows; line++)
            {
                int itemIndex = frame.ListState.ScrollTop + line;
                if (itemIndex >= items.Count)
                    break;

                WriteRow(
                    items[itemIndex],
                    contentBounds.X,
                    contentBounds.Y + 2 + line,
                    contentBounds.Width,
                    itemIndex == frame.ListState.SelectedIndex);
            }

            if (frame.ScrollbarBounds is { } scrollbarBounds)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    context.Canvas,
                    scrollbarBounds,
                    new ScrollState
                    {
                        TotalItems = items.Count,
                        ViewportItems = frame.ListState.ViewportRows,
                        FirstVisibleIndex = frame.ListState.ScrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    PaletteStyles.DialogBorder(_palette));
            }
        });
    }

    private readonly record struct DriveDialogFrame(
        IReadOnlyList<VolumeSelectionItem> Items,
        Rect Bounds,
        Rect ListBounds,
        Rect? ScrollbarBounds,
        ScrollableListFrameState ListState);

    private readonly record struct DriveDialogLayout(Rect FrameBounds, Rect ListBounds);

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

    private void WriteRow(VolumeSelectionItem item, int x, int y, int innerWidth, bool selected)
    {
        if (innerWidth <= 0)
            return;

        var normalStyle = selected ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
        var highlightStyle = selected ? PaletteStyles.InputHighlight(_palette) : PaletteStyles.DialogHighlight(_palette);

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

    private static string ItemText(VolumeSelectionItem item)
    {
        string displayName = item.Volume?.DisplayName ?? item.Label;
        string kind = item.Volume is null ? string.Empty : KindLabel(item.Volume.Kind, item.Volume.Status);
        return $"{displayName} {kind}".Trim();
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
            VolumeStatus.NotReady => "not ready",
            VolumeStatus.Disconnected => "disconnected",
            VolumeStatus.Error => "error",
            _ => kind switch
            {
                VolumeKind.Fixed => "fixed",
                VolumeKind.Removable => "removable",
                VolumeKind.Network => "network",
                VolumeKind.CdRom => "cdrom",
                VolumeKind.Ram => "ram",
                VolumeKind.MountPoint => "mount",
                VolumeKind.Pseudo => "pseudo",
                _ => "unknown",
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
            : (bytes, "B");

        string num = value >= 100 ? $"{value:F0}"
                   : value >= 10 ? $"{value:F1}"
                   : $"{value:F2}";

        num = num.Replace('.', ',');
        return $"{num} {unit}";
    }

    private PopupRenderOptions DriveOuterOptions =>
        PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false };

    private PopupRenderOptions DriveFrameOptions =>
        PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false };
}
