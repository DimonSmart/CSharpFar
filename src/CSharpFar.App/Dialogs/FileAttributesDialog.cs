using System.Globalization;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class FileAttributesDialog : IFileAttributesDialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 25;
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm:ss";

    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;
    private readonly IClock _clock;
    private readonly bool _canOpenSystemProperties;

    public FileAttributesDialog(DialogService dialogs, FormFieldFactory fields, IClock? clock = null, bool canOpenSystemProperties = false)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
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
            .Select(descriptor => CreateAttributeRow(snapshot, descriptor))
            .ToList();
        TextField creation = _fields.Text("creation", FormatTime(snapshot.CreationTime), width: DateTimeFormat.Length);
        TextField write = _fields.Text("write", FormatTime(snapshot.LastWriteTime), width: DateTimeFormat.Length);
        TextField access = _fields.Text("access", FormatTime(snapshot.LastAccessTime), width: DateTimeFormat.Length);
        creation.Enabled = snapshot.CanEditCreationTime;
        write.Enabled = snapshot.CanEditLastWriteTime;
        access.Enabled = snapshot.CanEditLastAccessTime;
        TriStateMatrixFormRow? permissions = snapshot.UnixMetadata is null ? null : FormControls.TriStateMatrix(
            "permissions",
            [new("read", "Read"), new("write", "Write"), new("execute", "Exec")],
            [
                new("owner", "Owner", [ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OwnerRead]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OwnerWrite]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OwnerExecute])]),
                new("group", "Group", [ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.GroupRead]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.GroupWrite]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.GroupExecute])]),
                new("other", "Others", [ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OthersRead]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OthersWrite]), ToCheckState(snapshot.UnixMetadata.PermissionStates[UnixPermissionBit.OthersExecute])]),
            ]);
        if (permissions is not null)
            permissions.Enabled = snapshot.UnixMetadata!.CanEditPermissions;
        var specialPermissionRows = snapshot.UnixMetadata?.PermissionStates
            .Where(static pair => pair.Key is UnixPermissionBit.SetUid or UnixPermissionBit.SetGid or UnixPermissionBit.Sticky)
            .ToDictionary(static pair => pair.Key, pair =>
            {
                TriStateCheckBoxRow row = FormControls.TriStateCheckBox($"permission-{pair.Key}", PermissionColumnLabel(pair.Key), ToCheckState(pair.Value));
                row.Enabled = snapshot.UnixMetadata!.CanEditPermissions;
                row.DisabledReason = snapshot.UnixMetadata.PermissionsDisabledReason;
                return row;
            })
            ?? new Dictionary<UnixPermissionBit, TriStateCheckBoxRow>();
        var unixSpecialRows = new[] { UnixPermissionBit.SetUid, UnixPermissionBit.SetGid, UnixPermissionBit.Sticky }
            .Where(specialPermissionRows.ContainsKey)
            .Select(bit => new UnixPermissionDialogRow(bit, specialPermissionRows[bit]))
            .ToList();
        string? error = null;
        var buttons = FormControls.Buttons("actions", _canOpenSystemProperties
            ?
            [
                DialogButton.Default("set", "Set", 'S'),
                DialogButton.Action("properties", "System properties", 'P'),
                DialogButton.Cancel(),
            ]
            : [DialogButton.Default("set", "Set", 'S'), DialogButton.Cancel()]);

        return _dialogs.Form(
            new FormDialogOptions("File attributes", DialogWidth, DialogHeight, 48, 8),
            rows: () => BuildRows(snapshot, attributeRows, permissions, unixSpecialRows, creation, write, access),
            footer: () => FormFooter.ErrorAndButtons(() => error, buttons),
            (result) =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<FileAttributesDialogResult?>.Complete(null);

                if (result.IsSubmitted)
                {
                    if (IsTimeAction(result.SourceRowId, result.Command))
                    {
                        ApplyTimeAction(result.SourceRowId!, result.Command!, snapshot, creation, write, access, _clock.Now);
                        return FormDialogOutcome<FileAttributesDialogResult?>.Continue();
                    }

                    switch (result.Command)
                    {
                        case "properties":
                            return FormDialogOutcome<FileAttributesDialogResult?>.Complete(
                                new FileAttributesDialogResult(EmptyChangeSet(), OpenSystemProperties: true));
                        case "set":
                        case null:
                            var states = attributeRows.ToDictionary(row => row.Descriptor.Id, row => ToAttributeEditState(row.Row.Value));
                            var unixStates = BuildUnixPermissionStates(permissions, unixSpecialRows);
                            var changeSet = CreateChangeSet(snapshot, states, unixStates, creation.Text, write.Text, access.Text, out error);
                            if (error is null)
                            {
                                return FormDialogOutcome<FileAttributesDialogResult?>.Complete(
                                    new FileAttributesDialogResult(changeSet, OpenSystemProperties: false));
                            }
                            return FormDialogOutcome<FileAttributesDialogResult?>.Continue();
                    }
                }

                return FormDialogOutcome<FileAttributesDialogResult?>.Continue();
            });
    }

    private IReadOnlyList<FormRow> BuildRows(
        FileMetadataSnapshot snapshot,
        IReadOnlyList<AttributeDialogRow> attributeRows,
        TriStateMatrixFormRow? permissions,
        IReadOnlyList<UnixPermissionDialogRow> unixSpecialRows,
        TextField creation,
        TextField write,
        TextField access)
    {
        var rows = new List<FormRow>
        {
            FormControls.Label("Change file attributes for"),
            FormControls.Label(snapshot.DisplayName),
            FormControls.Spacer(),
        };

        rows.AddRange(attributeRows.Select(static row => (FormRow)row.Row));

        if (snapshot.UnixMetadata is { } unixMetadata)
        {
            rows.Add(FormControls.Spacer());
            rows.Add(FormControls.Label("Unix permissions:"));
            rows.Add(permissions!);
            rows.AddRange(unixSpecialRows.Select(static row => (FormRow)row.Row));
            rows.Add(FormControls.Label($"Owner: {unixMetadata.OwnerName ?? unixMetadata.Uid?.ToString(CultureInfo.InvariantCulture) ?? "<not available>"}"));
            rows.Add(FormControls.Label($"Group: {unixMetadata.GroupName ?? unixMetadata.Gid?.ToString(CultureInfo.InvariantCulture) ?? "<not available>"}"));
            rows.Add(FormControls.Label($"Mode: {FormatUnixMode(unixMetadata)}"));
        }

        rows.Add(FormControls.Spacer());
        rows.Add(FormControls.Label("Date/Time:"));
        AddTimeRows(rows, "write:", write, snapshot.LastWriteTime, snapshot.CanEditLastWriteTime);
        AddTimeRows(rows, "creation:", creation, snapshot.CreationTime, snapshot.CanEditCreationTime);
        AddTimeRows(rows, "access:", access, snapshot.LastAccessTime, snapshot.CanEditLastAccessTime);
        rows.Add(FormControls.Spacer());
        if (snapshot.UnixMetadata is null)
        {
            rows.Add(FormControls.Label("Owner:"));
            rows.Add(FormControls.Label(snapshot.OwnerDisplayName ?? "<not available>"));
        }
        rows.Add(FormControls.Spacer());
        return rows;
    }

    private static void AddTimeRows(
        List<FormRow> rows,
        string label,
        TextField field,
        DateTime? original,
        bool enabled)
    {
        rows.Add(enabled
            ? FormControls.Text(label, field,
            [
                DialogButton.Action("original", "Original", 'O'),
                DialogButton.Action("current", "Current", 'U'),
                DialogButton.Action("blank", "Blank", 'B'),
            ])
            : FormControls.Text(label, field));
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

    internal static string FormatUnixMode(UnixFileMetadata metadata)
    {
        if (metadata.PermissionStates.Values.Any(static state => state == AttributeEditState.Indeterminate))
            return "<mixed>";
        return UnixPermissionFormatter.ToDisplayString(metadata.Permissions);
    }

    private static AttributeDialogRow CreateAttributeRow(
        FileMetadataSnapshot snapshot,
        FileAttributeDescriptor descriptor)
    {
        AttributeEditState state = snapshot.AttributeStates.TryGetValue(descriptor.Id, out var value)
            ? value
            : AttributeEditState.Unchecked;
        TriStateCheckBoxRow row = FormControls.TriStateCheckBox(
            $"attribute-{descriptor.Id}",
            descriptor.Label,
            ToCheckState(state));
        row.Enabled = descriptor.IsEditable;
        row.DisabledReason = descriptor.DisabledReason;
        return new AttributeDialogRow(descriptor, row);
    }

    private static CheckState ToCheckState(AttributeEditState value) => value switch
    {
        AttributeEditState.Checked => CheckState.Checked,
        AttributeEditState.Indeterminate => CheckState.Indeterminate,
        _ => CheckState.Unchecked,
    };

    private static AttributeEditState ToAttributeEditState(CheckState value) => value switch
    {
        CheckState.Checked => AttributeEditState.Checked,
        CheckState.Indeterminate => AttributeEditState.Indeterminate,
        _ => AttributeEditState.Unchecked,
    };

    private static IReadOnlyDictionary<UnixPermissionBit, AttributeEditState> BuildUnixPermissionStates(
        TriStateMatrixFormRow? permissions,
        IReadOnlyList<UnixPermissionDialogRow> specialRows)
    {
        var states = specialRows.ToDictionary(row => row.Bit, row => ToAttributeEditState(row.Row.Value));
        if (permissions is null)
            return states;
        states[UnixPermissionBit.OwnerRead] = ToAttributeEditState(permissions.GetValue("owner", "read"));
        states[UnixPermissionBit.OwnerWrite] = ToAttributeEditState(permissions.GetValue("owner", "write"));
        states[UnixPermissionBit.OwnerExecute] = ToAttributeEditState(permissions.GetValue("owner", "execute"));
        states[UnixPermissionBit.GroupRead] = ToAttributeEditState(permissions.GetValue("group", "read"));
        states[UnixPermissionBit.GroupWrite] = ToAttributeEditState(permissions.GetValue("group", "write"));
        states[UnixPermissionBit.GroupExecute] = ToAttributeEditState(permissions.GetValue("group", "execute"));
        states[UnixPermissionBit.OthersRead] = ToAttributeEditState(permissions.GetValue("other", "read"));
        states[UnixPermissionBit.OthersWrite] = ToAttributeEditState(permissions.GetValue("other", "write"));
        states[UnixPermissionBit.OthersExecute] = ToAttributeEditState(permissions.GetValue("other", "execute"));
        return states;
    }

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

}
