using System.Globalization;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>Maps the semantic conflict form result to a file-operation decision.</summary>
internal sealed class ConflictDialog
{
    private const string OverwriteButton = "overwrite";
    private const string MergeButton = "merge";
    private const string ReplaceButton = "replace";
    private const string SkipButton = "skip";
    private const string RenameButton = "rename";

    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public ConflictDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public FileOperationConflictDecision Show(FileOperationConflict conflict)
    {
        var mode = ConflictDialogMode.ChooseAction;
        var rememberChoice = FormControls.CheckBox("Remember choice");
        bool directoryConflict = conflict.SourceIsDirectory && conflict.DestinationIsDirectory;
        var chooseActions = FormControls.Buttons(CreateButtons(directoryConflict));
        var renameDestination = _fields.Text(new TextFieldOptions(
            InitialText: conflict.DestinationPath,
            SubmitOnEnter: true,
            HistoryValueMode: TextHistoryValueMode.PreserveWhitespace));
        var renameActions = FormControls.Buttons(
            DialogButton.Default(RenameButton, "Rename", 'R'),
            DialogButton.Cancel("Cancel", 'C'));
        string? renameError = null;

        IReadOnlyList<FormRow> Rows() =>
            mode == ConflictDialogMode.ChooseAction
                ?
                [
                    FormControls.Label(directoryConflict ? "Directory already exists" : "File already exists", TextAlignment.Center),
                    FormControls.Label(conflict.DestinationPath, TextAlignment.Center),
                    FormControls.Spacer(),
                    FormControls.Value("New", () => BuildInfo(conflict.SourceSize, conflict.SourceLastWriteTime)),
                    FormControls.Value("Existing", () => BuildInfo(conflict.DestinationSize, conflict.DestinationLastWriteTime)),
                    FormControls.Separator(),
                    rememberChoice,
                ]
                :
                [
                    FormControls.Label(directoryConflict ? "Directory already exists" : "File already exists", TextAlignment.Center),
                    FormControls.Label(conflict.DestinationPath, TextAlignment.Center),
                    FormControls.Spacer(),
                    FormControls.Value("New", () => BuildInfo(conflict.SourceSize, conflict.SourceLastWriteTime)),
                    FormControls.Value("Existing", () => BuildInfo(conflict.DestinationSize, conflict.DestinationLastWriteTime)),
                    FormControls.Separator(),
                    FormControls.Text("New destination:", renameDestination),
                ];

        IReadOnlyList<FormRow> Footer() =>
            mode == ConflictDialogMode.ChooseAction
                ? [chooseActions]
                : FormFooter.ErrorAndButtons(() => renameError, renameActions);

        return _dialogs.Form(
            new FormDialogOptions("Warning", PreferredWidth: 78, PreferredHeight: 14)
            {
                Appearance = DialogAppearance.Warning,
                InitialFocus = chooseActions,
            },
            rows: Rows,
            footer: Footer,
            handle: dialogEvent =>
            {
                if (dialogEvent.IsCancelled)
                {
                    if (mode == ConflictDialogMode.Rename)
                    {
                        mode = ConflictDialogMode.ChooseAction;
                        renameError = null;
                        return FormDialogOutcome<FileOperationConflictDecision>.ContinueWithFocus(chooseActions);
                    }

                    return FormDialogOutcome<FileOperationConflictDecision>.Complete(
                        FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel));
                }

                if (mode == ConflictDialogMode.ChooseAction)
                {
                    if (dialogEvent.Command == RenameButton)
                    {
                        if (rememberChoice.Value)
                        {
                            return FormDialogOutcome<FileOperationConflictDecision>.Complete(
                                FileOperationConflictDecision.FromMode(ConflictDecisionMode.RenameAll));
                        }

                        mode = ConflictDialogMode.Rename;
                        renameError = null;
                        return FormDialogOutcome<FileOperationConflictDecision>.ContinueWithFocus(renameDestination);
                    }

                    return dialogEvent.Command is { } command
                        ? FormDialogOutcome<FileOperationConflictDecision>.Complete(BuildDecision(command, rememberChoice.Value))
                        : FormDialogOutcome<FileOperationConflictDecision>.Continue();
                }

                if (dialogEvent.IsValueChangedFrom(renameDestination))
                {
                    renameError = null;
                    return FormDialogOutcome<FileOperationConflictDecision>.Continue();
                }

                if (dialogEvent.Command == RenameButton || dialogEvent.IsSubmitted)
                {
                    if (string.IsNullOrWhiteSpace(renameDestination.Text))
                    {
                        renameError = "Destination must not be empty.";
                        return FormDialogOutcome<FileOperationConflictDecision>.ContinueWithFocus(renameDestination);
                    }

                    if (string.Equals(renameDestination.Text, conflict.DestinationPath, StringComparison.Ordinal))
                    {
                        renameError = "New destination must be different from the existing destination.";
                        return FormDialogOutcome<FileOperationConflictDecision>.ContinueWithFocus(renameDestination);
                    }

                    return FormDialogOutcome<FileOperationConflictDecision>.Complete(
                        new FileOperationConflictDecision
                        {
                            Mode = ConflictDecisionMode.Rename,
                            NewDestinationPath = renameDestination.Text,
                        });
                }

                return FormDialogOutcome<FileOperationConflictDecision>.Continue();
            });
    }

    private static FileOperationConflictDecision BuildDecision(string buttonId, bool rememberChoice) =>
        buttonId switch
        {
            OverwriteButton => FileOperationConflictDecision.FromMode(rememberChoice ? ConflictDecisionMode.OverwriteAll : ConflictDecisionMode.Overwrite),
            MergeButton => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Merge),
            ReplaceButton => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Replace),
            SkipButton => FileOperationConflictDecision.FromMode(rememberChoice ? ConflictDecisionMode.SkipAll : ConflictDecisionMode.Skip),
            _ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
        };

    private static IReadOnlyList<DialogButton> CreateButtons(bool directoryConflict) =>
        directoryConflict
            ?
            [
                DialogButton.Default(MergeButton, "Merge", 'M'),
                DialogButton.Action(ReplaceButton, "Replace", 'P'),
                DialogButton.Action(RenameButton, "Rename", 'R'),
                DialogButton.Action(SkipButton, "Skip", 'S'),
                DialogButton.Cancel("Cancel", 'C'),
            ]
            :
            [
                DialogButton.Default(OverwriteButton, "Overwrite", 'O'),
                DialogButton.Action(SkipButton, "Skip", 'S'),
                DialogButton.Action(RenameButton, "Rename", 'R'),
                DialogButton.Cancel("Cancel", 'C'),
            ];

    private static string BuildInfo(long? size, DateTime? lastWriteTime) =>
        `${FormatSize(size)} ${FormatDate(lastWriteTime)}`.TrimEnd();

    private static string FormatSize(long? size) => size is null ? "n/a" : size.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
    private static string FormatDate(DateTime? time) => time is null ? string.Empty : time.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private enum ConflictDialogMode
    {
        ChooseAction,
        Rename,
    }
}
