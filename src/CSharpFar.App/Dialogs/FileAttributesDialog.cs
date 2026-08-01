using System.Globalization;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class FileAttributesDialog : IFileAttributesDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 25;
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm:ss";

    private readonly ModalFormHost _formDialogs;
    private readonly FormFieldFactory _fields;
    private readonly IClock _clock;
    private readonly bool _canOpenSystemProperties;

    public FileAttributesDialog(ModalDialogHost modalDialogs, FormFieldFactory fields, IClock? clock = null, bool canOpenSystemProperties = false)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _clock = clock ?? new SystemClock();
        _canOpenSystemProperties = canOpenSystemProperties;
    }

    public FileAttributesDialogResult? Show(FileMetadataSnapshot snapshot)
    {
        return RunLoop(snapshot);
    }

    internal static FileMetadataChangeSet CreateChangeSet(
        FileMetadataSnapshot original,
        IReadOnlyDictionary<FileAttributeId, AttributeEditState> currentAttributeStates,
        IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> currentUnixPermissionStates,
        string creationText,
        string writeText,
        string accessText,
        out string? error)
    {
        error = null;
        var changes = new Dictionary<FileAttributeId, AttributeEditState>();
        foreach (var descriptor in original.AttributesDescriptors.Where(static descriptor => descriptor.IsEditable))
        {
            var before = original.AttributeStates.TryGetValue(descriptor.Id, out var state)
                ? state
                : AttributeEditState.Unchecked;
            var after = currentAttributeStates.TryGetValue(descriptor.Id, out var current)
                ? current
                : before;
            if (after != before && after != AttributeEditState.Indeterminate)
                changes[descriptor.Id] = after;
        }

        DateTime? creation = ParseChangedTime(
            "creation",
            creationText,
            original.CreationTime,
            original.CanEditCreationTime,
            ref error);
        DateTime? write = ParseChangedTime(
            "write",
            writeText,
            original.LastWriteTime,
            original.CanEditLastWriteTime,
            ref error);
        DateTime? access = ParseChangedTime(
            "access",
            accessText,
            original.LastAccessTime,
            original.CanEditLastAccessTime,
            ref error);

        var unixChanges = new Dictionary<UnixPermissionBit, AttributeEditState>();
        if (original.UnixMetadata is { CanEditPermissions: true } unixMetadata)
        {
            foreach (UnixPermissionBit bit in Enum.GetValues<UnixPermissionBit>())
            {
                AttributeEditState before = unixMetadata.PermissionStates[bit];
                AttributeEditState after = currentUnixPermissionStates.TryGetValue(bit, out var current) ? current : before;
                if (after != before && after != AttributeEditState.Indeterminate)
                    unixChanges[bit] = after;
            }
        }

        return new FileMetadataChangeSet(changes, creation, write, access, unixChanges);
    }

    internal static string FormatTime(DateTime? value) =>
        value is null ? string.Empty : value.Value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private FileAttributesDialogResult? RunLoop(FileMetadataSnapshot snapshot)
    {
        var attributeRows = snapshot.AttributesDescriptors
            .Select(descriptor => new AttributeDialogRow(
                descriptor,
                new TriStateCheckBoxRow(new TriStateCheckBoxLine(
                    descriptor.Label,
                    snapshot.AttributeStates.TryGetValue(descriptor.Id, out var state) ? state : AttributeEditState.Unchecked,
                    descriptor.IsEditable))))
            .ToList();
        TextField creation = _fields.Text("creation", FormatTime(snapshot.CreationTime), width: DateTimeFormat.Length);
        TextField write = _fields.Text("write", FormatTime(snapshot.LastWriteTime), width: DateTimeFormat.Length);
        TextField access = _fields.Text("access", FormatTime(snapshot.LastAccessTime), width: DateTimeFormat.Length);
        var permissionLines = snapshot.UnixMetadata?.PermissionStates.ToDictionary(
            static pair => pair.Key,
            pair => new TriStateCheckBoxLine(
                PermissionColumnLabel(pair.Key),
                pair.Value,
                snapshot.UnixMetadata.CanEditPermissions))
            ?? new Dictionary<UnixPermissionBit, TriStateCheckBoxLine>();
        var unixMatrixRows = snapshot.UnixMetadata is null
            ? []
            : new List<UnixPermissionMatrixRow>
            {
                MatrixRow("Owner", UnixPermissionBit.OwnerRead, UnixPermissionBit.OwnerWrite, UnixPermissionBit.OwnerExecute, permissionLines),
                MatrixRow("Group", UnixPermissionBit.GroupRead, UnixPermissionBit.GroupWrite, UnixPermissionBit.GroupExecute, permissionLines),
                MatrixRow("Others", UnixPermissionBit.OthersRead, UnixPermissionBit.OthersWrite, UnixPermissionBit.OthersExecute, permissionLines),
            };
        var unixSpecialRows = new[] { UnixPermissionBit.SetUid, UnixPermissionBit.SetGid, UnixPermissionBit.Sticky }
            .Where(permissionLines.ContainsKey)
            .Select(bit => new UnixPermissionDialogRow(bit, new TriStateCheckBoxRow(permissionLines[bit])))
            .ToList();
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(BuildRows(snapshot, attributeRows, unixMatrixRows, unixSpecialRows, creation, write, access, error));

        return _formDialogs.Run(
            form,
            new ModalFormOptions("File attributes", DialogWidth, DialogHeight, 48, 8),
            static layout => new ModalFormLayout(layout.ContentBounds),
            (routed, result) =>
            {
                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<FileAttributesDialogResult?>.Complete(null);

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
                {
                    if (IsTimeAction(routed.Target?.Value, result.Command))
                    {
                        ApplyTimeAction(routed.Target!.Value, result.Command!, snapshot, creation, write, access, _clock.Now);
                        return ModalDialogLoopResult<FileAttributesDialogResult?>.ContinueChanged;
                    }

                    switch (result.Command)
                    {
                        case "properties":
                            return ModalDialogLoopResult<FileAttributesDialogResult?>.Complete(
                                new FileAttributesDialogResult(EmptyChangeSet(), OpenSystemProperties: true));
                        case "set":
                        case null:
                            var states = attributeRows.ToDictionary(row => row.Descriptor.Id, row => row.Row.Value);
                            var unixStates = permissionLines.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value);
                            var changeSet = CreateChangeSet(snapshot, states, unixStates, creation.Text, write.Text, access.Text, out error);
                            if (error is null)
                            {
                                return ModalDialogLoopResult<FileAttributesDialogResult?>.Complete(
                                    new FileAttributesDialogResult(changeSet, OpenSystemProperties: false));
                            }
                            return ModalDialogLoopResult<FileAttributesDialogResult?>.ContinueChanged;
                    }
                }

                return ModalDialogLoopResult<FileAttributesDialogResult?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private IReadOnlyList<IFormRow> BuildRows(
        FileMetadataSnapshot snapshot,
        IReadOnlyList<AttributeDialogRow> attributeRows,
        IReadOnlyList<UnixPermissionMatrixRow> unixMatrixRows,
        IReadOnlyList<UnixPermissionDialogRow> unixSpecialRows,
        TextField creation,
        TextField write,
        TextField access,
        string? error)
    {
        var fill = FarDialogStyles.Fill;
        var disabled = FarDialogStyles.DisabledControl(fill);
        var rows = new List<IFormRow>
        {
            new LabelRow("Change file attributes for", fill),
            new LabelRow(snapshot.DisplayName, fill),
            new SeparatorRow(fill, drawLine: false),
        };

        rows.AddRange(attributeRows.Select(row => row.Descriptor.IsEditable
            ? (IFormRow)row.Row
            : new LabelRow(FormatDisabledAttribute(row), disabled)));

        if (snapshot.UnixMetadata is { } unixMetadata)
        {
            rows.Add(new SeparatorRow(fill, drawLine: false));
            rows.Add(new LabelRow("Unix permissions:", fill));
            if (!unixMetadata.CanEditPermissions && unixMetadata.PermissionsDisabledReason is { Length: > 0 } reason)
                rows.Add(new LabelRow(reason, disabled));
            rows.Add(new LabelRow("          Read        Write       Exec", fill));
            rows.AddRange(unixMatrixRows);
            rows.AddRange(unixSpecialRows.Select(row => unixMetadata.CanEditPermissions
                ? (IFormRow)row.Row
                : new LabelRow(FormatDisabledPermission(row), disabled)));
            rows.Add(new LabelRow($"Owner: {unixMetadata.OwnerName ?? unixMetadata.Uid?.ToString(CultureInfo.InvariantCulture) ?? "<not available>"}", fill));
            rows.Add(new LabelRow($"Group: {unixMetadata.GroupName ?? unixMetadata.Gid?.ToString(CultureInfo.InvariantCulture) ?? "<not available>"}", fill));
            rows.Add(new LabelRow($"Mode: {FormatUnixMode(unixMetadata)}", fill));
        }

        rows.Add(new SeparatorRow(fill, drawLine: false));
        rows.Add(new LabelRow("Date/Time:", fill));
        AddTimeRows(rows, "write:", write, snapshot.LastWriteTime, snapshot.CanEditLastWriteTime, disabled);
        AddTimeRows(rows, "creation:", creation, snapshot.CreationTime, snapshot.CanEditCreationTime, disabled);
        AddTimeRows(rows, "access:", access, snapshot.LastAccessTime, snapshot.CanEditLastAccessTime, disabled);
        rows.Add(new SeparatorRow(fill, drawLine: false));
        if (snapshot.UnixMetadata is null)
        {
            rows.Add(new LabelRow("Owner:", fill));
            rows.Add(new LabelRow(snapshot.OwnerDisplayName ?? "<not available>", fill));
        }
        rows.Add(new SeparatorRow(fill, drawLine: false));

        var buttons = _canOpenSystemProperties
            ? new[]
            {
                new DialogButton("set", "Set", 'S', IsDefault: true),
                new DialogButton("properties", "System properties", 'P'),
                new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
            }
            : [new DialogButton("set", "Set", 'S', IsDefault: true), new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel)];
        rows.Add(new LabelRow(error ?? string.Empty, FarDialogStyles.Error));
        rows.Add(new ButtonRow(buttons));
        return rows;
    }

    private static void AddTimeRows(
        List<IFormRow> rows,
        string label,
        TextField field,
        DateTime? original,
        bool enabled,
        CellStyle disabled)
    {
        if (!enabled)
        {
            rows.Add(new LabelRow($"{label} {FormatTime(original)}", disabled));
            return;
        }

        rows.Add(new TextInputWithButtonsRow(
            label.PadRight(10),
            field,
            [
                new DialogButton("original", "Original", 'O'),
                new DialogButton("current", "Current", 'U'),
                new DialogButton("blank", "Blank", 'B'),
            ],
            buttonAreaWidth: 36));
    }

    private static DateTime? ParseChangedTime(
        string label,
        string text,
        DateTime? original,
        bool editable,
        ref string? error)
    {
        if (error is not null || !editable)
            return null;
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!DateTime.TryParseExact(
                text.Trim(),
                DateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            error = $"{label} time must use {DateTimeFormat}.";
            return null;
        }

        return value == original ? null : value;
    }

    private static string FormatDisabledAttribute(AttributeDialogRow row)
    {
        string marker = row.Row.Value switch
        {
            AttributeEditState.Checked => "x",
            AttributeEditState.Indeterminate => "-",
            _ => " ",
        };
        string reason = row.Descriptor.DisabledReason is { Length: > 0 } value ? $" - {value}" : string.Empty;
        return $"[{marker}] {row.Descriptor.Label}{reason}";
    }

    private static string FormatDisabledPermission(UnixPermissionDialogRow row)
    {
        string marker = row.Row.Value switch
        {
            AttributeEditState.Checked => "x",
            AttributeEditState.Indeterminate => "-",
            _ => " ",
        };
        return $"[{marker}] {PermissionLabel(row.Bit)}";
    }

    private static string PermissionLabel(UnixPermissionBit bit) => bit switch
    {
        UnixPermissionBit.OwnerRead => "Owner read",
        UnixPermissionBit.OwnerWrite => "Owner write",
        UnixPermissionBit.OwnerExecute => "Owner execute",
        UnixPermissionBit.GroupRead => "Group read",
        UnixPermissionBit.GroupWrite => "Group write",
        UnixPermissionBit.GroupExecute => "Group execute",
        UnixPermissionBit.OthersRead => "Others read",
        UnixPermissionBit.OthersWrite => "Others write",
        UnixPermissionBit.OthersExecute => "Others execute",
        UnixPermissionBit.SetUid => "Set UID",
        UnixPermissionBit.SetGid => "Set GID",
        UnixPermissionBit.Sticky => "Sticky",
        _ => bit.ToString(),
    };

    internal static string FormatUnixMode(UnixFileMetadata metadata)
    {
        if (metadata.PermissionStates.Values.Any(static state => state == AttributeEditState.Indeterminate))
            return "<mixed>";
        return UnixPermissionFormatter.ToDisplayString(metadata.Permissions);
    }

    private static UnixPermissionMatrixRow MatrixRow(
        string label,
        UnixPermissionBit read,
        UnixPermissionBit write,
        UnixPermissionBit execute,
        IReadOnlyDictionary<UnixPermissionBit, TriStateCheckBoxLine> lines) =>
        new(label, lines[read], lines[write], lines[execute]);

    private static string PermissionColumnLabel(UnixPermissionBit bit) => bit switch
    {
        UnixPermissionBit.SetUid => "Set UID",
        UnixPermissionBit.SetGid => "Set GID",
        UnixPermissionBit.Sticky => "Sticky",
        UnixPermissionBit.OwnerRead or UnixPermissionBit.GroupRead or UnixPermissionBit.OthersRead => "Read",
        UnixPermissionBit.OwnerWrite or UnixPermissionBit.GroupWrite or UnixPermissionBit.OthersWrite => "Write",
        UnixPermissionBit.OwnerExecute or UnixPermissionBit.GroupExecute or UnixPermissionBit.OthersExecute => "Exec",
        _ => bit.ToString(),
    };

    private static bool IsTimeAction(string? fieldId, string? action) =>
        fieldId is "creation" or "write" or "access" &&
        action is "original" or "current" or "blank";

    internal static bool ApplyTimeAction(
        string fieldId,
        string action,
        FileMetadataSnapshot snapshot,
        TextField creation,
        TextField write,
        TextField access,
        DateTime now)
    {
        TextField? field = fieldId switch
        {
            "creation" when snapshot.CanEditCreationTime => creation,
            "write" when snapshot.CanEditLastWriteTime => write,
            "access" when snapshot.CanEditLastAccessTime => access,
            _ => null,
        };
        DateTime? original = fieldId switch
        {
            "creation" => snapshot.CreationTime,
            "write" => snapshot.LastWriteTime,
            "access" => snapshot.LastAccessTime,
            _ => null,
        };
        if (field is null)
            return false;

        switch (action)
        {
            case "original":
                field.Text = FormatTime(original);
                return true;
            case "current":
                field.Text = FormatTime(now);
                return true;
            case "blank":
                field.Text = string.Empty;
                return true;
            default:
                return false;
        }
    }

    private static FileMetadataChangeSet EmptyChangeSet() =>
        new(
            new Dictionary<FileAttributeId, AttributeEditState>(),
            null,
            null,
            null,
            new Dictionary<UnixPermissionBit, AttributeEditState>());

    private sealed record AttributeDialogRow(
        FileAttributeDescriptor Descriptor,
        TriStateCheckBoxRow Row);

    private sealed record UnixPermissionDialogRow(
        UnixPermissionBit Bit,
        TriStateCheckBoxRow Row);

    private sealed class UnixPermissionMatrixRow : FormRow
    {
        private const int LabelWidth = 9;
        private const int ColumnWidth = 12;
        private readonly string _label;
        private readonly TriStateCheckBoxLine[] _columns;
        private int _focusedColumn;

        internal UnixPermissionMatrixRow(
            string label,
            TriStateCheckBoxLine read,
            TriStateCheckBoxLine write,
            TriStateCheckBoxLine execute)
        {
            _label = label;
            _columns = [read, write, execute];
        }

        public override void Render(FormRowRenderContext context)
        {
            context.Canvas.Write(
                context.Bounds.X,
                context.Bounds.Y,
                _label.PadRight(Math.Min(LabelWidth, context.Bounds.Width)),
                FarDialogStyles.Fill);
            for (int index = 0; index < _columns.Length; index++)
            {
                int x = context.Bounds.X + LabelWidth + index * ColumnWidth;
                int width = Math.Min(ColumnWidth, context.Bounds.Right - x);
                if (width > 0)
                    _columns[index].Render(context.Canvas, x, context.Bounds.Y, width, context.Focused && index == _focusedColumn);
            }
        }

        public override bool IsFocusable => _columns.Any(static column => column.Enabled);

        public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
        {
            if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                int delta = key.Key == ConsoleKey.LeftArrow ? -1 : 1;
                _focusedColumn = (_focusedColumn + delta + _columns.Length) % _columns.Length;
                return FormInputResult.Handled;
            }

            return _columns[_focusedColumn].TryHandleKey(key)
                ? FormInputResult.Handled
                : FormInputResult.NotHandled;
        }

        public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
        {
            for (int index = 0; index < _columns.Length; index++)
            {
                int x = context.Bounds.X + LabelWidth + index * ColumnWidth;
                int width = Math.Min(ColumnWidth, context.Bounds.Right - x);
                if (width <= 0 ||
                    !_columns[index].TryHandleMouse(mouse, new Rect(x, context.Bounds.Y, width, 1)))
                    continue;
                _focusedColumn = index;
                return FormInputResult.Handled;
            }

            return FormInputResult.NotHandled;
        }
    }
}
