using CSharpFar.Ui;

namespace CSharpFar.Ui.Demo;

internal sealed record NewCommitInput(string Author, string Subject, string Type, bool Amend, bool SignOff);

internal sealed class CommitWorkflows
{
    private static readonly string[] CommitTypes = ["Commit", "Feature", "Fix", "Docs"];
    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;
    private readonly ITextClipboard _clipboard;

    public CommitWorkflows(DialogService dialogs, FormFieldFactory fields, ITextClipboard clipboard)
    {
        _dialogs = dialogs;
        _fields = fields;
        _clipboard = clipboard;
    }

    public NewCommitInput? CreateCommit(string initialAuthor)
    {
        TextField subject = _fields.Text(new TextFieldOptions(SubmitOnEnter: true));
        TextField author = _fields.Text(new TextFieldOptions(InitialText: initialAuthor));
        ChoiceFormRow<string> type = FormControls.Choice("&Type", CommitTypes, value => value, CommitTypes[0]);
        CheckBoxRow amend = FormControls.CheckBox("&Amend previous commit");
        CheckBoxRow signOff = FormControls.CheckBox("&Sign off commit");
        ButtonRow actions = FormControls.Buttons(
            DialogButton.Default("create", "Create", 'C'),
            DialogButton.Cancel());

        return _dialogs.Form(
            new FormDialogOptions("Create Commit", PreferredWidth: 68, PreferredHeight: 13, SubmitOnEnter: true)
            {
                InitialFocus = subject,
                ResizeMode = DialogResizeMode.Both,
            },
            () =>
            [
                FormControls.Text("&Subject", subject),
                FormControls.Text("&Author", author,
                    [DialogButton.Auxiliary("default-author", "Default", 'D')]),
                FormControls.Separator(),
                type,
                amend,
                signOff,
                FormControls.Spacer(),
            ],
            () => [actions],
            submit: () => string.IsNullOrWhiteSpace(subject.Text)
                ? FormSubmit.Invalid<NewCommitInput>("Subject is required.", subject)
                : FormSubmit.Success(new NewCommitInput(author.TrimmedText.Length == 0 ? initialAuthor : author.TrimmedText,
                    subject.TrimmedText, type.Value, amend.Value, signOff.Value)),
            auxiliary: formEvent =>
            {
                if (!formEvent.IsAuxiliary || formEvent.Command != "default-author") return false;
                author.Text = initialAuthor;
                author.SelectAll();
                return true;
            });
    }

    public void ShowDetails(DemoCommit commit)
    {
        var files = new TableList<DemoChangedFile>(commit.ChangedFiles, new TableListDefinition<DemoChangedFile>
        {
            Columns =
            [
                TableColumn<DemoChangedFile>.Text("Status", file => file.Change.ToString(), TableWidth.Fixed(9), emphasized: true),
                TableColumn<DemoChangedFile>.Text("Path", file => file.Path, TableWidth.Flexible(60, 10)),
                TableColumn<DemoChangedFile>.Text("Added", file => file.Additions.ToString(), TableWidth.Fixed(7), TableColumnAlignment.Right),
                TableColumn<DemoChangedFile>.Text("Deleted", file => file.Deletions.ToString(), TableWidth.Fixed(7), TableColumnAlignment.Right),
            ]
        });
        var form = new ScrollableFormDialog();
        form.SetRows(
        [
            FormControls.Value("Hash", () => commit.Hash),
            FormControls.Value("Branch", () => commit.Branch),
            FormControls.Value("Author", () => commit.Author),
            FormControls.Value("Date", () => commit.Timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz")),
            FormControls.Value("Subject", () => commit.Subject),
            FormControls.Separator(),
        ],
        [FormControls.Buttons(DialogButton.Action("copy", "Copy Hash", 'H'), DialogButton.Cancel("Close", 'C'))]);

        _dialogs.Composite(
            new CompositeDialogOptions("Commit Details", PreferredWidth: 94, PreferredHeight: 23, MinHeight: 10)
            {
                ResizeMode = DialogResizeMode.Both,
            },
            form,
            files,
            () => $"{commit.ChangedFiles.Count} files, {commit.ChangedFiles.Sum(file => file.Additions)} additions, {commit.ChangedFiles.Sum(file => file.Deletions)} deletions",
            new Dictionary<ConsoleKey, string> { [ConsoleKey.F5] = "copy" },
            formEvent =>
            {
                if (formEvent.Kind == CompositeDialogEventKind.Cancelled)
                    return CompositeDialogOutcome<bool>.Complete(false);
                if (formEvent.Kind == CompositeDialogEventKind.Command && formEvent.Command == "copy")
                {
                    _clipboard.TrySetText(commit.Hash);
                    return CompositeDialogOutcome<bool>.ContinueChanged;
                }
                return CompositeDialogOutcome<bool>.ContinueNoChange;
            });
    }

    public bool CopyHash(DemoCommit commit) => _clipboard.TrySetText(commit.Hash);

    public bool ConfirmDelete(DemoCommit commit) =>
        _dialogs.Confirm("Delete Commit", "Delete the selected fake commit?", $"{commit.Hash}  {commit.Subject}");
}
