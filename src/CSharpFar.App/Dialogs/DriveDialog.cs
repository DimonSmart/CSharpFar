using System.Globalization;
using System.Text;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DriveDialog
{
    private const int DialogWidth = 48;
    private const string SystemVolumesAction = "system-volumes";
    private const string DetailsAction = "details";
    private readonly DialogService _dialogs;

    public DriveDialog(ModalDialogHost modalDialogs, DialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(modalDialogs);
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public VolumeSelectionItem? Show(IReadOnlyList<VolumeSelectionItem> items, int initialCursor = 0, bool? macVolumes = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) { _dialogs.Message("Change drive", "No volumes found."); return null; }

        bool mac = macVolumes ?? OperatingSystem.IsMacOS();
        VolumeSelectionItem initialItem = items[Math.Clamp(initialCursor, 0, items.Count - 1)];
        // The active panel's mount remains visible even when macOS classifies it as technical.
        VolumeSelectionItem? contextual = mac && initialItem.Volume?.Visibility == VolumeVisibility.System ? initialItem : null;
        bool showSystem = false;
        DriveDialogRow[] rows = BuildRows();

        int initial = Array.FindIndex(rows, row => ReferenceEquals(row.Item, initialItem));
        if (initial < 0) initial = 0;
        int presentationRows = items.Count + 4;

        // Register the union of shortcuts, but resolve the command only against current visible rows.
        // A hidden system volume cannot be activated by an old shortcut.
        IReadOnlyDictionary<ConsoleKey, string> shortcuts = ShortcutCommands(ProjectRows(items, mac));
        var commands = new Dictionary<ConsoleKey, string>(shortcuts);
        if (mac)
        {
            commands[ConsoleKey.F9] = SystemVolumesAction;
            commands[ConsoleKey.F3] = DetailsAction;
        }

        return _dialogs.Table(new TableDialogOptions<DriveDialogRow, VolumeSelectionItem?>
        {
            Title = "Change drive",
            Items = () => rows,
            Definition = new TableListDefinition<DriveDialogRow>
            {
                Columns = mac
                    ? [
                        TableColumn<DriveDialogRow>.Text("Volume / Mount point", FormatDisk, TableWidth.Grow(PreferredNameWidth(), 14), emphasized: true),
                        TableColumn<DriveDialogRow>.Text("Free", row => BuildSizeCols(row.Item.Volume).Free, TableWidth.Optional(10, 10, priority: 1), alignment: TableColumnAlignment.Right),
                        TableColumn<DriveDialogRow>.Text("Total", row => BuildSizeCols(row.Item.Volume).Total, TableWidth.Optional(10, 10, priority: 0), alignment: TableColumnAlignment.Right),
                    ]
                    : [
                        TableColumn<DriveDialogRow>.Text("Disk", FormatDisk, width: 18, emphasized: true),
                        TableColumn<DriveDialogRow>.Text("Free", row => BuildSizeCols(row.Item.Volume).Free, width: 10, alignment: TableColumnAlignment.Right),
                        TableColumn<DriveDialogRow>.Text("Total", row => BuildSizeCols(row.Item.Volume).Total, width: 10, alignment: TableColumnAlignment.Right),
                    ],
                SectionBreakBetween = static (previous, current) => previous.Group != current.Group,
            },
            ItemIdentity = row => Identity(row.Item),
            InitialSelectedIndex = initial,
            PreferredWidth = mac ? PreferredWidth() : DialogWidth,
            PreferredWidthProvider = mac ? PreferredWidth : null,
            PreferredHeight = Math.Min(presentationRows + 6, 24),
            MinWidth = 20,
            MinHeight = 6,
            Appearance = DialogAppearance.Popup,
            TableAppearance = ListAppearance.Menu,
            DefaultItemActionId = "select",
            CancelKeys = [ConsoleKey.Escape, ConsoleKey.F10],
            Cancel = () => null,
            Actions = mac ? [DialogButton.Auxiliary(SystemVolumesAction, "System volumes", 'S')] : [],
            StatusText = mac ? row =>
            {
                string suffix = showSystem ? "shown" : "hidden";
                string path = row?.Item.Volume?.RootPath ?? row?.Item.Label ?? string.Empty;
                return $"F3 Details  {SafeText(path)}  | System volumes: {suffix}";
            } : null,
            PreserveStatusEnd = mac,
            KeyboardCommands = commands,
            HandleAction = Handle,
        });

        DriveDialogRow[] BuildRows() => ProjectRows(items, mac, showSystem, contextual);

        int PreferredNameWidth() => Math.Clamp(
            Math.Max(ConsoleTextMetrics.GetCellWidth("Volume / Mount point"),
                rows.Length == 0 ? 0 : rows.Max(row => ConsoleTextMetrics.GetCellWidth(FormatDisk(row)))),
            14, 108);

        int PreferredWidth() => Math.Clamp(PreferredNameWidth() + 10 + 10 + 6 + 1 + 4, 72, 140);

        DialogOutcome<VolumeSelectionItem?> Handle(ListDialogActionContext<DriveDialogRow> action)
        {
            if (action.ActionId == "select")
                return action.SelectedItem is { } selected ? TryCompleteSelection(selected.Item) : DialogOutcome<VolumeSelectionItem?>.ContinueOpen();

            if (mac && action.ActionId == DetailsAction)
            {
                if (action.SelectedItem?.Item.Volume is { } vol)
                    _dialogs.Message("Volume details", $"Mount point: {SafeText(vol.RootPath)}\nName: {SafeText(vol.DisplayName)}\nVisibility: {vol.Visibility}");
                return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();
            }

            if (mac && action.ActionId == SystemVolumesAction)
            {
                string? previous = action.SelectedItem is null ? null : Identity(action.SelectedItem.Item);
                showSystem = !showSystem;
                rows = BuildRows();
                int selected = previous is null ? -1 : Array.FindIndex(rows, row => Identity(row.Item) == previous);
                if (selected < 0)
                    selected = Array.FindIndex(rows, row => row.Item.Volume?.RootPath == "/");
                return DialogOutcome<VolumeSelectionItem?>.RefreshOpen(Math.Max(0, selected));
            }

            if (!action.ActionId.StartsWith("shortcut:", StringComparison.Ordinal))
                return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();

            string shortcut = action.ActionId["shortcut:".Length..];
            int[] matches = Enumerable.Range(0, rows.Length)
                .Where(index => string.Equals(rows[index].EffectiveShortcut, shortcut, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0) return DialogOutcome<VolumeSelectionItem?>.ContinueOpen();
            if (matches.Length == 1) return TryCompleteSelection(rows[matches[0]].Item);

            int currentMatch = Array.IndexOf(matches, action.SelectedIndex);
            int next = currentMatch >= 0 ? matches[(currentMatch + 1) % matches.Length] : matches[0];
            return DialogOutcome<VolumeSelectionItem?>.ChangeSelection(next);
        }
    }

    private static string Identity(VolumeSelectionItem item) =>
        item.Action == VolumeSelectionAction.OpenVolume
            ? "volume:" + (item.Volume?.Id ?? item.Volume?.RootPath ?? item.Label)
            : "module:" + (item.ModuleActionId?.ToString("D") ?? item.Label);

    private static DriveDialogRow[] ProjectRows(IReadOnlyList<VolumeSelectionItem> items, bool mac = false,
        bool showSystem = true, VolumeSelectionItem? contextual = null)
    {
        VolumeSelectionItem[] volumes = items.Where(item => item.Action == VolumeSelectionAction.OpenVolume)
            .Where(item => !mac || showSystem || item.Volume?.Visibility != VolumeVisibility.System || ReferenceEquals(item, contextual))
            .ToArray();

        if (mac)
        {
            volumes = volumes.OrderBy(item => item.Volume?.RootPath == "/" ? 0 :
                    item.Volume?.Visibility switch
                    {
                        VolumeVisibility.User => 1,
                        VolumeVisibility.Unknown => 2,
                        VolumeVisibility.System => 3,
                        _ => 2,
                    })
                .ThenBy(item => item.Volume?.RootPath, StringComparer.Ordinal).ToArray();
        }

        VolumeSelectionItem[] modules = items.Where(item => item.Action == VolumeSelectionAction.OpenModule).ToArray();
        return volumes.Select(item => new DriveDialogRow(item, item.Shortcut, Group(item, mac), mac))
            .Concat(modules.Select((item, index) => new DriveDialogRow(item, index < 10 ? index.ToString() : null, 4, mac)))
            .ToArray();
    }

    private static int Group(VolumeSelectionItem item, bool mac) =>
        !mac ? 0 : item.Volume?.RootPath == "/" ? 0 : item.Volume?.Visibility switch
        {
            VolumeVisibility.System => 3,
            VolumeVisibility.Unknown => 2,
            _ => 1,
        };

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

    private static string FormatDisk(DriveDialogRow row)
    {
        if (row.Item.Volume is { } volume)
        {
            if (!row.IsMac) return $"{volume.DisplayName} {KindLabel(volume.Kind, volume.Status)}".Trim();
            string root = SafeText(volume.RootPath);
            string leaf = volume.RootPath == "/" ? "/" : Path.GetFileName(volume.RootPath.TrimEnd('/'));
            string name = SafeText(volume.DisplayName);
            if (!string.IsNullOrWhiteSpace(name) && name != leaf && name != volume.RootPath)
                root += " — " + name;
            string kind = volume.Status switch
            {
                VolumeStatus.NotReady => "not ready",
                VolumeStatus.Disconnected => "disconnected",
                _ => volume.Kind == VolumeKind.Network ? "network" :
                    volume.Kind == VolumeKind.Removable ? "removable" : string.Empty,
            };
            return kind.Length == 0 ? root : root + " (" + kind + ")";
        }
        string prefix = row.EffectiveShortcut is null ? "   " : $"{row.EffectiveShortcut}: ";
        return prefix + SafeText(row.Item.Label);
    }

    // Avoid injecting control sequences or unprintable scalars from a volume name/path into the console.
    private static string SafeText(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            sb.Append(category is UnicodeCategory.Control or UnicodeCategory.Surrogate or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                ? "�" : rune.ToString());
        }
        return sb.ToString();
    }

    private static (string Free, string Total) BuildSizeCols(FileSystemVolume? vol)
    {
        if (vol is null || vol.Status is VolumeStatus.NotReady or VolumeStatus.Disconnected or VolumeStatus.Error)
            return (string.Empty, string.Empty);
        return (vol.FreeBytes is >= 0 ? FormatBytes(vol.FreeBytes.Value) : string.Empty,
            vol.TotalBytes is >= 0 ? FormatBytes(vol.TotalBytes.Value) : string.Empty);
    }

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

    private sealed record DriveDialogRow(VolumeSelectionItem Item, string? EffectiveShortcut, int Group, bool IsMac);
}
