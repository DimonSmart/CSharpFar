using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal enum EditorReplaceAction
{
    Replace,
    ReplaceAll,
}

internal sealed record EditorReplaceDialogResult(
    EditorSearchOptions Search,
    string Replacement,
    EditorReplaceAction Action);

internal sealed class EditorReplaceDialog
{
    private const int Width = 56;
    private const int MinimumHeight = 12;

    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public EditorReplaceDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public EditorReplaceDialogResult? Show(EditorSearchOptions? previousSearch, string previousReplacement)
    {
        TextField pattern = _fields.Text(new TextFieldOptions(
            previousSearch?.Pattern ?? string.Empty,
            AppTextHistoryIds.EditorFindPattern,
            SubmitOnEnter: true));
        TextField replacement = _fields.Text(new TextFieldOptions(
            previousReplacement,
            AppTextHistoryIds.EditorReplaceText,
            HistoryValueMode: TextHistoryValueMode.PreserveWhitespace));

        IReadOnlyList<SearchOptionLine> optionLines = EditorSearchDialogSupport.CreateOptionLines(previousSearch);
        CheckBoxRow[] checkboxes = optionLines
            .Select(option => FormControls.CheckBox(option.Label, option.IsChecked))
            .ToArray();

        ButtonRow buttons = FormControls.Buttons(
            DialogButton.Default("replace", "Replace", 'R'),
            DialogButton.Action("replace-all", "Replace all", 'A'),
            DialogButton.Cancel());

        string? error = null;

        return _dialogs.Form<EditorReplaceDialogResult?>(
            new FormDialogOptions(
                "Replace",
                PreferredWidth: Width,
                MinWidth: 40,
                MinHeight: MinimumHeight)
            {
                InitialFocus = pattern,
            },
            rows: () =>
            [
                FormControls.Label("Find"),
                FormControls.Text(pattern),
                FormControls.Label("Replace with"),
                FormControls.Text(replacement),
                .. checkboxes,
            ],
            footer: () => FormFooter.ErrorAndButtons(() => error, buttons),
            handle: formEvent =>
            {
                if (formEvent.IsCancelled)
                    return FormDialogOutcome<EditorReplaceDialogResult?>.Complete(null);

                if (formEvent.IsValueChanged)
                {
                    error = null;
                    return FormDialogOutcome<EditorReplaceDialogResult?>.Continue();
                }

                if (!formEvent.IsSubmitted)
                    return FormDialogOutcome<EditorReplaceDialogResult?>.Continue();

                bool GetOption(string id)
                {
                    for (int i = 0; i < optionLines.Count; i++)
                    {
                        if (string.Equals(optionLines[i].Id, id, StringComparison.Ordinal))
                            return checkboxes[i].Value;
                    }

                    return false;
                }

                EditorSearchOptions search = EditorSearchDialogSupport.CreateOptions(pattern.Text, GetOption);
                error = EditorSearchDialogSupport.Validate(search);
                if (error is not null)
                    return FormDialogOutcome<EditorReplaceDialogResult?>.ContinueWithFocus(pattern);

                pattern.AcceptHistory();
                replacement.AcceptHistory();

                var action = string.Equals(formEvent.Command, "replace-all", StringComparison.Ordinal)
                    ? EditorReplaceAction.ReplaceAll
                    : EditorReplaceAction.Replace;

                return FormDialogOutcome<EditorReplaceDialogResult?>.Complete(
                    new EditorReplaceDialogResult(search, replacement.Text, action));
            });
    }
}
