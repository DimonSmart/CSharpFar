using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DriveDialog
{
    private const int DialogWidth = 48;
    private readonly DialogService _dialogs;

    public DriveDialog(ModalDialogHost modalDialogs, DialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) { _dialogs.Message("Change drive", "No volumes found."); return null; }

        DriveDialogRow[] rows = ProjectRows(items);
        int initial = rows.Select((row, index) => (row, index)).FirstOrDefault(pair => ReferenceEquals(pair.row.Item, items[Math.Clamp(initialCursor, 0, items.Count - 1)])).index;
        int presentationRows = rows.Length + (rows.Any(row => row.Item.Action == VolumeSelectionAction.OpenVolume) && rows.Any(row => row.Item.Action == VolumeSelectionAction.OpenModule) ? 1 : 0);

        return _dialogs.Table(new TableDialogOptions<DriveDialogRow, VolumeSelectionItem?>
        {
            Title = "Change drive",
            Items = () => rows,
            Definition = new TableListDefinition<DriveDialogRow>
            {
                Columns =
                [
                    TableColumn<DriveDialogRow>.Text("Disk", FormatDisk, width: 18, emphasized: true),
                    TableColumn<DriveDialogRow>.Text("Free", row => BuildSizeCols(row.Item.Volume).Free, width: 10, alignment: TableColumnAlignment.Right),
                    TableColumn<DriveDialogRow>.Text("Total", row => BuildSizeCols(row.Item.Volume).Total, width: 10, alignment: TableColumnAlignment.Right),
                ],
                SectionBreakBetween = static (previous, current) => previous.Item.Action != current.Item.Action,
            },
            InitialSelectedIndex = initial,
            PreferredWidth = DialogWidth,
            PreferredHeight = Math.Min(presentationRows + 6, 24),
            MinWidth = 20,
            MinHeight = 6,
            Appearance = DialogAppearance.Popup,
            TableAppearance = ListAppearance.Menu,
            DefaultItemActionId = "select",
            CancelKeys = [ConsoleKey.Escape, ConsoleKey.F10],
            Cancel = () => null,
            KeyboardCommands = ShortcutCommands(rows),
            HandleAction = action => HandleAction(action, rows),
        });
    }

    private DialogOutcome<VolumeSelectionItem?> HandleAction(ListDialogActionContext<DriveDialogRow> action, IReadOnlyList<DriveDialogRow> rows)
    {
        if (action.ActionId == "select")
            return action.SelectedItem is { } selected ? TryCompleteSelection(selected.Item) : DialogOutcome<VolumeSelectionItem?>.ContinueOpen();
        if (!action.ActionId.StartsWith("shortcut:", StringComparison.Ordinal))
            return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();

        string shortcut = action.ActionId["shortcut:".Length..];
        int[] matches = Enumerable.Range(0, rows.Count).Where(index => string.Equals(rows[index].EffectiveShortcut, shortcut, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();
        if (matches.Length == 1) return TryCompleteSelection(rows[matches[0]].Item);

        int currentMatch = Array.IndexOf(matches, action.SelectedIndex);
        int next = currentMatch >= 0 ? matches[(currentMatch + 1) % matches.Length] : matches[0];
        return DialogOutcome<VolumeSelectionItem?>.ChangeSelection(next);
    }

    private static IReadOnlyDictionary<ConsoleKey, string> ShortcutCommands(IEnumerable<DriveDialogRow> rows)
    {
        var commands = new Dictionary<ConsoleKey, string>();
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

    private DialogOutcome<VolumeSelectionItem?> TryCompleteSelection(VolumeSelectionItem selected)
    {
        if (selected.Volume is { } volume && !IsSelectable(volume.Status))
        {
            string statusText = volume.Status switch { VolumeStatus.NotReady => "not ready", VolumeStatus.Disconnected => "disconnected", _ => "error" };
            _dialogs.Message("Change drive", $"{volume.DisplayName}: volume is {statusText}.");
            return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();
        }
        return DialogOutcome<VolumeSelectionItem?>.Complete(selected);
    }

    private static DriveDialogRow[] ProjectRows(IReadOnlyList<VolumeSelectionItem> items)
    {
        VolumeSelectionItem[] volumes = items.Where(item => item.Action == VolumeSelectionAction.OpenVolume).ToArray();
        VolumeSelectionItem[] modules = items.Where(item => item.Action == VolumeSelectionAction.OpenModule).ToArray();
        return volumes.Select(item => new DriveDialogRow(item, item.Shortcut)).Concat(modules.Select((item, index) => new DriveDialogRow(item, index < 10 ? index.ToString() : null))).ToArray();
    }

    private static string FormatDisk(DriveDialogRow row)
    {
        if (row.Item.Volume is { } volume) return $"{volume.DisplayName} {KindLabel(volume.Kind, volume.Status)}".Trim();
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
