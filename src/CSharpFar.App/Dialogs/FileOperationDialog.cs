using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record FileOperationDialogResult(
    string Destination,
    FileOperationOptions Options);

internal sealed class FileOperationDialog
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 26;

    private static readonly ConflictDecisionMode[] CopyConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
        ConflictDecisionMode.OnlyNewer,
    ];

    private static readonly ConflictDecisionMode[] MoveConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
    ];

    private static readonly CopyMode[] LocalCopyModes =
    [
        CopyMode.Normal,
        CopyMode.Reliable,
        CopyMode.FastSalvage,
    ];

    private static readonly FileSecurityMode[] SecurityModes =
    [
        FileSecurityMode.Default,
        FileSecurityMode.CopyAccessControl,
        FileSecurityMode.Inherit,
    ];

    private readonly FormFieldFactory _fields;
    private readonly DialogService _dialogs;

    public FileOperationDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    internal FileOperationDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
        : this(new DialogService(modalDialogs, fields), fields)
    {
    }

    public FileOperationDialogResult? ShowCopy(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? $"Copy {Path.GetFileName(sources[0])} to:"
            : $"Copy {sources.Count} items to:";
        return Show("Copy", prompt, "Copy", initialDestination, initialOptions, CopyConflictModes, LocalCopyModes, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowMove(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? "Move / Rename to:"
            : $"Move {sources.Count} items to:";
        return Show("Move", prompt, "Move", initialDestination, initialOptions, MoveConflictModes, copyModes: null, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowRename(
        string source,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string sourceName = Path.GetFileName(source);
        string prompt = string.IsNullOrEmpty(sourceName)
            ? "Rename to:"
            : $"Rename {sourceName} to:";
        return Show("Rename", prompt, "Rename", initialDestination, initialOptions, MoveConflictModes, copyModes: null, showOperationOptions: false);
    }

    private FileOperationDialogResult? Show(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        IReadOnlyList<CopyMode>? copyModes,
        bool showOperationOptions)
    {
        return RunLoop(title, prompt, actionLabel, initialDestination, initialOptions, conflictModes, copyModes, showOperationOptions);
    }

    private FileOperationDialogResult? RunLoop(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        IReadOnlyList<CopyMode>? copyModes,
        bool showOperationOptions)
    {
        TextField destination = _fields.Text(new TextFieldOptions(
            initialDestination,
            AppTextHistoryIds.FileOperationDestination,
            SubmitOnEnter: true));
        TextField filter = _fields.Text(new TextFieldOptions(
            string.IsNullOrWhiteSpace(initialOptions.FileMask) ? "*" : initialOptions.FileMask,
            AppTextHistoryIds.FileOperationFilter,
            SubmitOnEnter: true));

        var securityChoice = FormControls.Choice(
            "Access rights:", SecurityModes, SecurityModeLabel, initialOptions.SecurityMode);
        ChoiceFormRow<CopyMode>? copyModeChoice = copyModes is null
            ? null
            : FormControls.Choice(
                "Copy mode:", copyModes, CopyModeLabel, initialOptions.CopyMode);
        var conflictChoiceRow = FormControls.MultiLineChoice(
            string.Empty,
            conflictModes,
            ConflictLabel,
            initialOptions.DefaultConflictDecision,
            itemsPerRow: 4);
        var preserveTimestamps = FormControls.CheckBox(
            "Preserve all timestamps", initialOptions.PreserveTimestamps);
        var preserveAttributes = FormControls.CheckBox(
            "Preserve attributes", initialOptions.PreserveAttributes);
        var copySymlinkContents = FormControls.CheckBox(
            "Copy contents of symbolic links",
            initialOptions.SymlinkMode == SymlinkCopyMode.CopyTargetContents);
        var useFilter = FormControls.CheckBox(
            "Use filter", !string.IsNullOrWhiteSpace(initialOptions.FileMask));
        var buttons = FormControls.Buttons(
            DialogButton.Default("submit", actionLabel, actionLabel[0]),
            DialogButton.Cancel());
        string? error = null;
        return _dialogs.Form(
            new FormDialogOptions(title, DialogWidth, DialogHeight, 40, 8),
            rows: () => BuildRows(prompt, destination, filter, securityChoice, copyModeChoice, conflictChoiceRow, preserveTimestamps, preserveAttributes, copySymlinkContents, useFilter, showOperationOptions),
            footer: () => FormFooter.ErrorAndButtons(() => error, buttons),
            (result) =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<FileOperationDialogResult?>.Complete(null);

                if (result.IsSubmitted)
                {
                    var dialogResult = BuildResult(
                        destination,
                        filter,
                        initialOptions,
                        conflictChoiceRow.Value,
                        copyModeChoice?.Value ?? CopyMode.Normal,
                        securityChoice.Value,
                        preserveTimestamps.Value,
                        preserveAttributes.Value,
                        copySymlinkContents.Value,
                        useFilter.Value,
                        ref error);
                    if (dialogResult is not null)
                        return FormDialogOutcome<FileOperationDialogResult?>.Complete(dialogResult);
                }

                return FormDialogOutcome<FileOperationDialogResult?>.Continue();
            });
    }

    private static IReadOnlyList<FormRow> BuildRows(
        string prompt,
        TextField destination,
        TextField filter,
        ChoiceFormRow<FileSecurityMode> securityChoice,
        ChoiceFormRow<CopyMode>? copyModeChoice,
        MultiLineChoiceFormRow<ConflictDecisionMode> conflictChoiceRow,
        CheckBoxRow preserveTimestamps,
        CheckBoxRow preserveAttributes,
        CheckBoxRow copySymlinkContents,
        CheckBoxRow useFilter,
        bool showOperationOptions)
    {
        var rows = new List<FormRow>
        {
            FormControls.Label(prompt),
            FormControls.Text(destination),
            FormControls.Spacer(),
        };

        if (showOperationOptions)
        {
            if (copyModeChoice is not null)
            {
                rows.Add(copyModeChoice);
                rows.Add(FormControls.Spacer());
            }

            rows.Add(securityChoice);
            rows.Add(FormControls.Spacer());
        }

        rows.Add(FormControls.Label("Already existing files:"));
        rows.Add(conflictChoiceRow);

        if (showOperationOptions)
        {
            rows.Add(FormControls.Spacer());
            rows.Add(preserveTimestamps);
            rows.Add(preserveAttributes);
            rows.Add(copySymlinkContents);
            rows.Add(FormControls.Spacer());
            rows.Add(useFilter);
            rows.Add(FormControls.Label("Filter mask:"));
            filter.Enabled = useFilter.Value;
            rows.Add(FormControls.Text(filter));
            rows.Add(FormControls.Spacer());
        }

        return rows;
    }

    private static FileOperationDialogResult? BuildResult(
        TextField destination,
        TextField filter,
        FileOperationOptions initialOptions,
        ConflictDecisionMode conflictMode,
        CopyMode copyMode,
        FileSecurityMode securityMode,
        bool preserveTimestamps,
        bool preserveAttributes,
        bool copySymlinkContents,
        bool useFilter,
        ref string? error)
    {
        string destinationText = destination.TrimmedText;
        if (string.IsNullOrWhiteSpace(destinationText))
        {
            error = "Destination must not be empty.";
            return null;
        }

        error = null;
        string? mask = useFilter && !string.IsNullOrWhiteSpace(filter.Text)
            ? filter.TrimmedText
            : null;

        destination.AcceptHistory();
        if (mask is not null)
            filter.AcceptHistory();

        return new FileOperationDialogResult(
            destinationText,
            initialOptions with
            {
                DefaultConflictDecision = conflictMode,
                CopyMode = copyMode,
                SecurityMode = securityMode,
                PreserveTimestamps = preserveTimestamps,
                PreserveAttributes = preserveAttributes,
                SymlinkMode = copySymlinkContents ? SymlinkCopyMode.CopyTargetContents : SymlinkCopyMode.CopyLink,
                FileMask = mask,
            });
    }

    private static string ConflictLabel(ConflictDecisionMode mode) => mode switch
    {
        ConflictDecisionMode.Overwrite => "Overwrite",
        ConflictDecisionMode.OverwriteAll => "Overwrite all",
        ConflictDecisionMode.Skip => "Skip",
        ConflictDecisionMode.SkipAll => "Skip all",
        ConflictDecisionMode.Rename => "Rename",
        ConflictDecisionMode.RenameAll => "Rename all",
        ConflictDecisionMode.OnlyNewer => "Only newer",
        _ => "Ask",
    };

    private static string CopyModeLabel(CopyMode mode) => mode switch
    {
        CopyMode.Reliable => "Reliable",
        CopyMode.FastSalvage => "Fast salvage",
        _ => "Normal",
    };

    private static string SecurityModeLabel(FileSecurityMode mode) => mode switch
    {
        FileSecurityMode.CopyAccessControl => "Copy",
        FileSecurityMode.Inherit => "Inherit",
        _ => "Default",
    };

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }
}
