using System.Globalization;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>Maps the semantic conflict form result to a file-operation decision.</summary>
internal sealed class ConflictDialog
{
    private const string OverwriteButton = "overwrite";
    private const string SkipButton = "skip";
    private const string RenameButton = "rename";

    private readonly DialogService _dialogs;

    public ConflictDialog(DialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public FileOperationConflictDecision Show(FileOperationConflict conflict)
    {
        var rememberChoice = FormControls.CheckBox("Remember choice");
        var actions = FormControls.Buttons(CreateButtons());
        return _dialogs.Form(
            new FormDialogOptions("Warning", PreferredWidth: 78, PreferredHeight: 13)
            {
                Appearance = DialogAppearance.Warning,
                InitialFocus = actions,
            },
            rows: () =>
            [
                FormControls.Label("File already exists", TextAlignment.Center),
                FormControls.Label(conflict.DestinationPath, TextAlignment.Center),
                FormControls.Spacer(),
                FormControls.Value("New", () => BuildInfo(conflict.SourceSize, conflict.SourceLastWriteTime)),
                FormControls.Value("Existing", () => BuildInfo(conflict.DestinationSize, conflict.DestinationLastWriteTime)),
                FormControls.Separator(),
                rememberChoice,
            ],
            footer: () => [actions],
            handle: dialogEvent => dialogEvent.IsCancelled
                ? FormDialogOutcome<FileOperationConflictDecision>.Complete(FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel))
                : dialogEvent.Command is { } command
                    ? FormDialogOutcome<FileOperationConflictDecision>.Complete(BuildDecision(command, rememberChoice.Value, conflict))
                    : FormDialogOutcome<FileOperationConflictDecision>.Continue());
    }

    private FileOperationConflictDecision BuildDecision(string buttonId, bool rememberChoice, FileOperationConflict conflict) =>
        buttonId switch
        {
            OverwriteButton => FileOperationConflictDecision.FromMode(rememberChoice ? ConflictDecisionMode.OverwriteAll : ConflictDecisionMode.Overwrite),
            SkipButton => FileOperationConflictDecision.FromMode(rememberChoice ? ConflictDecisionMode.SkipAll : ConflictDecisionMode.Skip),
            RenameButton => BuildRenameDecision(rememberChoice, conflict),
            _ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
        };

    private FileOperationConflictDecision BuildRenameDecision(bool rememberChoice, FileOperationConflict conflict)
    {
        if (rememberChoice)
            return FileOperationConflictDecision.FromMode(ConflictDecisionMode.RenameAll);

        string? renamed = _dialogs.Input(new SingleLineInputDialogOptions { Title = "Rename", Prompt = "New destination:", InitialText = conflict.DestinationPath });
        return string.IsNullOrWhiteSpace(renamed)
            ? FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip)
            : new FileOperationConflictDecision { Mode = ConflictDecisionMode.Rename, NewDestinationPath = renamed };
    }

    private static IReadOnlyList<DialogButton> CreateButtons() =>
    [
        DialogButton.Default(OverwriteButton, "Overwrite", 'O'),
        DialogButton.Action(SkipButton, "Skip", 'S'),
        DialogButton.Action(RenameButton, "Rename", 'R'),
        DialogButton.Cancel("Cancel", 'C'),
    ];

    private static string BuildInfo(long? size, DateTime? lastWriteTime) =>
        $"{FormatSize(size)} {FormatDate(lastWriteTime)}".TrimEnd();

    private static string FormatSize(long? size) => size is null ? "n/a" : size.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
    private static string FormatDate(DateTime? time) => time is null ? string.Empty : time.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
}
