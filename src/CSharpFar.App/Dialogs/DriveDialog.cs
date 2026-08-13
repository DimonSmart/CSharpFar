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
    private static readonly TableListDefinition<VolumeSelectionItem> TableDefinition = new()
    {
        Columns =
        [
            TableColumn<VolumeSelectionItem>.Text("Disk", FormatDisk, width: 18, emphasized: true),
            TableColumn<VolumeSelectionItem>.Text("Free", item => BuildSizeCols(item.Volume).Free, width: 10, alignment: TableColumnAlignment.Right),
            TableColumn<VolumeSelectionItem>.Text("Total", item => BuildSizeCols(item.Volume).Total, width: 10, alignment: TableColumnAlignment.Right),
        ],
    };

    private readonly ModalDialogHost _modalDialogs;
    private readonly DialogService _dialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public DriveDialog(ModalDialogHost modalDialogs, DialogService dialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _palette = palette ?? PaletteRegistry.Default;
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        VolumeSelectionItem[] snapshot = items.ToArray();
        if (snapshot.Length == 0)
        {
            _dialogs.Message("Change drive", "No volumes found.");
            return null;
        }

        return RunLoop(snapshot, Math.Clamp(initialCursor, 0, snapshot.Length - 1));
    }

    private VolumeSelectionItem? RunLoop(VolumeSelectionItem[] items, int initialCursor)
    {
        var table = new TableList<VolumeSelectionItem>(items, TableDefinition, initialCursor);
        string? lastShortcut = null;

        return _modalDialogs.RunInteractive<DriveDialogFrame, ScrollableListInputResult, VolumeSelectionItem?>(
            (context, _) =>
            {
                DriveDialogFrame frame = BuildFrame(context.Size, items, table);
                RenderFrame(context, table, frame);
                return frame;
            },
            frame => table.BuildInteractionFrame(frame.Table),
            (input, frame, route) => RouteInput(input, frame, route, table),
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
                        int selectedIndex = table.SelectedIndex;
                        int scrollTop = routed.Frame.Table.ScrollTop;
                        string shortcut = key.KeyChar.ToString().ToUpperInvariant();
                        VolumeSelectionItem? immediate = HandleShortcut(items, table, shortcut, routed.Frame.Table.ViewportRows, ref lastShortcut);
                        if (immediate is not null)
                            return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(immediate);

                        return table.SelectedIndex != selectedIndex || table.CalculateFrame(routed.Frame.Table.Bounds).ScrollTop != scrollTop
                            ? ModalDialogLoopResult<VolumeSelectionItem?>.ContinueChanged
                            : ModalDialogLoopResult<VolumeSelectionItem?>.ContinueNoChange;
                    }
                }

                if (BreaksShortcutCycle(routed.Input, result))
                    lastShortcut = null;

                if (result.Kind == ScrollableListInputResultKind.Confirmed &&
                    table.TryGetSelectedItem(out VolumeSelectionItem selected))
                {
                    return TryCompleteSelection(selected);
                }

                return ModalDialogLoopResult<VolumeSelectionItem?>.ContinueNoChange;
            });
    }

    private static (ScrollableListInputResult Semantic, UiInputResult UiResult) RouteInput(
        ConsoleInputEvent input,
        DriveDialogFrame frame,
        UiInputRouteContext route,
        TableList<VolumeSelectionItem> table)
    {
        if (input is KeyConsoleInputEvent { Key: var key })
        {
            if (key.Key is ConsoleKey.Escape or ConsoleKey.F10)
                return (ScrollableListInputResult.Handled, UiInputResult.HandledResult);
        }

        return table.RouteInput(input, frame.Table, route);
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
            _dialogs.Message(
                "Change drive",
                $"{volume.DisplayName}: volume is {statusText}.");
            return ModalDialogLoopResult<VolumeSelectionItem?>.ContinueNoChange;
        }

        return ModalDialogLoopResult<VolumeSelectionItem?>.Complete(selected);
    }

    private static VolumeSelectionItem? HandleShortcut(
        IReadOnlyList<VolumeSelectionItem> items,
        TableList<VolumeSelectionItem> table,
        string shortcut,
        int visibleRows,
        ref string? lastShortcut)
    {
        var matches = new List<int>();
        for (int index = 0; index < items.Count; index++)
        {
            if (string.Equals(items[index].Shortcut, shortcut, StringComparison.OrdinalIgnoreCase))
                matches.Add(index);
        }

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
        {
            table.SetSelectedIndex(matches[0]);
            lastShortcut = shortcut;

            VolumeSelectionItem item = items[table.SelectedIndex];
            return item.Volume is null || IsSelectable(item.Volume.Status) ? item : null;
        }

        int startSearch = string.Equals(lastShortcut, shortcut, StringComparison.OrdinalIgnoreCase)
            ? table.SelectedIndex + 1
            : 0;
        int next = matches.FirstOrDefault(index => index >= startSearch, matches[0]);
        table.SetSelectedIndex(next);
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

    private DriveDialogFrame BuildFrame(
        ConsoleSize size,
        VolumeSelectionItem[] items,
        TableList<VolumeSelectionItem> table)
    {
        int requestedRows = Math.Min(items.Length, Math.Max(0, size.Height - 6));
        ModalDialogRenderer.Layout modal = _modalRenderer.CalculateLayout(size, DialogWidth, requestedRows + 6);
        DriveDialogLayout layout = CalculateLayout(modal, items.Length);
        return new DriveDialogFrame(modal, table.CalculateFrame(layout.TableBounds));
    }

    private static DriveDialogLayout CalculateLayout(ModalDialogRenderer.Layout modal, int itemCount)
    {
        Rect contentBounds = modal.ContentBounds;
        int visibleRows = Math.Min(itemCount, Math.Max(0, contentBounds.Height - 2));
        Rect tableBounds = visibleRows > 0 && contentBounds.Width > 0
            ? new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width, visibleRows + 2)
            : new Rect(contentBounds.X, contentBounds.Y, 0, 0);
        return new DriveDialogLayout(tableBounds);
    }

    private void RenderFrame(UiRenderContext context, TableList<VolumeSelectionItem> table, DriveDialogFrame frame)
    {
        _modalRenderer.Render(context.Canvas, frame.Modal, "Change drive", true, DriveOuterOptions, DriveFrameOptions, (_, _) =>
        {
            Rect frameBounds = frame.Modal.FrameBounds;
            const string hint = " Enter  Esc ";
            if (frameBounds.Width >= hint.Length && frameBounds.Height > 0)
            {
                int hintX = frameBounds.X + (frameBounds.Width - hint.Length) / 2;
                context.Canvas.Write(hintX, frameBounds.Y + frameBounds.Height - 1, hint, PaletteStyles.DialogTitle(_palette));
            }

            table.Render(context.Canvas, frame.Table);
        });
    }

    private readonly record struct DriveDialogFrame(
        ModalDialogRenderer.Layout Modal,
        TableListFrame Table);

    private readonly record struct DriveDialogLayout(Rect TableBounds);

    private static string FormatDisk(VolumeSelectionItem item)
    {
        string displayName = item.Volume?.DisplayName ?? item.Label;
        string kind = item.Volume is null ? string.Empty : KindLabel(item.Volume.Kind, item.Volume.Status);
        return $"{displayName} {kind}".Trim();
    }

    private static (string Free, string Total) BuildSizeCols(FileSystemVolume? vol)
    {
        if (vol?.Status == VolumeStatus.Ready && vol.TotalBytes.HasValue && vol.FreeBytes.HasValue)
        {
            string free = FormatBytes(vol.FreeBytes.Value);
            string total = FormatBytes(vol.TotalBytes.Value);
            return (free, total);
        }

        return (string.Empty, string.Empty);
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
