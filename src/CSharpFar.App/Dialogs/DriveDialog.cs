using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DriveDialog
{
    private const int DialogWidth = 48;
    private readonly DialogService _dialogs;
    private string? _cycledShortcut;

    public DriveDialog(ModalDialogHost modalDialogs, DialogService dialogs) => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) { _dialogs.Message("Change drive", "No volumes found."); return null; }
        _cycledShortcut = null;

        DriveDialogRow[] rows = ProjectRows(items);
        int initial = rows.Select((row, index) => (row, index)).FirstOrDefault(pair => ReferenceEquals(pair.row.Item, items[Math.Clamp(initialCursor, 0, items.Count - 1)])).index;
        var table = new TableList<DriveDialogRow>(rows, new TableListDefinition<DriveDialogRow>
        {
            Columns =
            [
                TableColumn<DriveDialogRow>.Text("Disk", FormatDisk, width: 18, emphasized: true),
                TableColumn<DriveDialogRow>.Text("Free", row => BuildSizeCols(row.Item.Volume).Free, width: 10, alignment: TableColumnAlignment.Right),
                TableColumn<DriveDialogRow>.Text("Total", row => BuildSizeCols(row.Item.Volume).Total, width: 10, alignment: TableColumnAlignment.Right),
            ],
            SectionBreakBetween = static (previous, current) => previous.Item.Action != current.Item.Action,
        }, initial, ListAppearance.Menu);
        int presentationRows = rows.Length + (rows.Any(row => row.Item.Action == VolumeSelectionAction.OpenVolume) && rows.Any(row => row.Item.Action == VolumeSelectionAction.OpenModule) ? 1 : 0);

        return _dialogs.Composite(
            new CompositeDialogOptions("Change drive", DialogWidth, Math.Min(presentationRows + 6, 24), 20, 6, Appearance: DialogAppearance.Popup),
            new ScrollableFormDialog(), table, status: null, commands: ShortcutCommands(rows),
            handle: semantic => HandleEvent(semantic, table, rows));
    }

    private CompositeDialogOutcome<VolumeSelectionItem?> HandleEvent(CompositeDialogEvent semantic, TableList<DriveDialogRow> table, IReadOnlyList<DriveDialogRow> rows)
    {
        if (semantic.Kind == CompositeDialogEventKind.Cancelled || semantic is { Kind: CompositeDialogEventKind.Command, Command: "cancel" }) return CompositeDialogOutcome<VolumeSelectionItem?>.Complete(null);
        if (semantic.Kind == CompositeDialogEventKind.ContentSelectionChanged) { _cycledShortcut = null; return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueChanged; }
        if (semantic.Kind == CompositeDialogEventKind.ContentConfirmed && table.TryGetSelectedItem(out DriveDialogRow row)) return TryCompleteSelection(row.Item);
        if (semantic.Kind != CompositeDialogEventKind.Command || semantic.Command is not { } command || !command.StartsWith("shortcut:", StringComparison.Ordinal)) return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueNoChange;
        string shortcut = command["shortcut:".Length..];
        int[] matches = Enumerable.Range(0, table.Count)
            .Where(index => string.Equals(rows[index].EffectiveShortcut, shortcut, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0) return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueNoChange;
        if (matches.Length == 1)
        {
            if (rows[matches[0]].Item.Volume is { } volume && !IsSelectable(volume.Status))
                return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueChanged;
            return TryCompleteSelection(rows[matches[0]].Item);
        }

        int currentMatch = Array.IndexOf(matches, table.SelectedIndex);
        int next = currentMatch >= 0 ? matches[(currentMatch + 1) % matches.Length] : matches[0];
        table.SetSelectedIndex(next);
        _cycledShortcut = shortcut;
        return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueChanged;
    }

    private static IReadOnlyDictionary<ConsoleKey, string> ShortcutCommands(IEnumerable<DriveDialogRow> rows)
    {
        var commands = new Dictionary<ConsoleKey, string>();
        commands[ConsoleKey.Escape] = "cancel";
        commands[ConsoleKey.F10] = "cancel";
        foreach (string shortcut in rows.Select(row => row.EffectiveShortcut).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string command = "shortcut:" + shortcut;
            if (shortcut.Length == 1 && char.IsDigit(shortcut[0]))
            {
                int digit = shortcut[0] - '0';
                commands[(ConsoleKey)((int)ConsoleKey.D0 + digit)] = command;
                commands[(ConsoleKey)((int)ConsoleKey.NumPad0 + digit)] = command;
            }
            else if (Enum.TryParse(shortcut, true, out ConsoleKey key)) commands[key] = command;
        }
        return commands;
    }

    private CompositeDialogOutcome<VolumeSelectionItem?> TryCompleteSelection(VolumeSelectionItem selected)
    {
        if (selected.Volume is { } volume && !IsSelectable(volume.Status))
        {
            string statusText = volume.Status switch { VolumeStatus.NotReady => "not ready", VolumeStatus.Disconnected => "disconnected", _ => "error" };
            _dialogs.Message("Change drive", $"{volume.DisplayName}: volume is {statusText}.");
            return CompositeDialogOutcome<VolumeSelectionItem?>.ContinueNoChange;
        }
        return CompositeDialogOutcome<VolumeSelectionItem?>.Complete(selected);
    }

    private static DriveDialogRow[] ProjectRows(IReadOnlyList<VolumeSelectionItem> items)
    {
        VolumeSelectionItem[] volumes = items.Where(item => item.Action == VolumeSelectionAction.OpenVolume).ToArray();
        VolumeSelectionItem[] modules = items.Where(item => item.Action == VolumeSelectionAction.OpenModule).ToArray();
        return volumes.Select(item => new DriveDialogRow(item, item.Shortcut)).Concat(modules.Select((item, index) => new DriveDialogRow(item, index < 10 ? index.ToString() : null))).ToArray();
    }

    private static string FormatDisk(DriveDialogRow row)
    {
        if (row.Item.Volume is { } volume)
            return $"{volume.DisplayName} {KindLabel(volume.Kind, volume.Status)}".Trim();
        string prefix = row.EffectiveShortcut is null ? "   " : $"{row.EffectiveShortcut}: ";
        return prefix + row.Item.Label;
    }

    private static (string Free, string Total) BuildSizeCols(FileSystemVolume? vol) => vol?.Status == VolumeStatus.Ready && vol.TotalBytes.HasValue && vol.FreeBytes.HasValue ? (FormatBytes(vol.FreeBytes.Value), FormatBytes(vol.TotalBytes.Value)) : (string.Empty, string.Empty);
    internal static string KindLabel(VolumeKind kind, VolumeStatus status) => status switch
    {
        VolumeStatus.NotReady => "not ready",
        VolumeStatus.Disconnected => "disconnected",
        VolumeStatus.Error => "error",
        _ => kind switch { VolumeKind.Fixed => "fixed", VolumeKind.Removable => "removable", VolumeKind.Network => "network", VolumeKind.CdRom => "cdrom", VolumeKind.Ram => "ram", VolumeKind.MountPoint => "mount", VolumeKind.Pseudo => "pseudo", _ => "unknown" }
    };
    private static bool IsSelectable(VolumeStatus status) => status is VolumeStatus.Ready or VolumeStatus.Unchecked;
    internal static string FormatBytes(long bytes)
    {
        const long TB = 1L << 40, GB = 1L << 30, MB = 1L << 20, KB = 1L << 10;
        (double value, string unit) = bytes >= TB ? ((double)bytes / TB, "T") : bytes >= GB ? ((double)bytes / GB, "G") : bytes >= MB ? ((double)bytes / MB, "M") : bytes >= KB ? ((double)bytes / KB, "K") : (bytes, "B");
        string number = value >= 100 ? $"{value:F0}" : value >= 10 ? $"{value:F1}" : $"{value:F2}";
        return $"{number.Replace('.', ',')} {unit}";
    }

    private sealed record DriveDialogRow(VolumeSelectionItem Item, string? EffectiveShortcut);
}
