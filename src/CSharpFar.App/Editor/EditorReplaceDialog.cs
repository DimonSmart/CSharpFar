using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal readonly record struct EditorReplaceCommandResult(bool IsSuccess, bool HasMatch, string? ErrorMessage = null)
{
    public static EditorReplaceCommandResult Success() => new(true, true);
    public static EditorReplaceCommandResult Completed(string? message = null) => new(true, false, message);
    public static EditorReplaceCommandResult Failure(string errorMessage) => new(false, false, errorMessage);
}

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

    public void Show(
        EditorSearchOptions? previousSearch,
        string previousReplacement,
        Func<EditorSearchOptions, EditorReplaceCommandResult> find,
        Func<EditorSearchOptions, string, EditorReplaceCommandResult> replace,
        Func<EditorSearchOptions, string, EditorReplaceCommandResult> replaceAll,
        Action invalidatePreview)
    {
        ArgumentNullException.ThrowIfNull(find);
        ArgumentNullException.ThrowIfNull(replace);
        ArgumentNullException.ThrowIfNull(replaceAll);
        ArgumentNullException.ThrowIfNull(invalidatePreview);

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

        bool replaceEnabled = false;
        bool findNext = false;
        ButtonRow buttons = FormControls.Buttons(CreateButtons(replaceEnabled, findNext));
        string? error = null;

        void SetButtonState(bool canReplace, bool showFindNext)
        {
            if (replaceEnabled == canReplace && findNext == showFindNext)
                return;

            replaceEnabled = canReplace;
            findNext = showFindNext;
            buttons.SetButtons(CreateButtons(replaceEnabled, findNext));
        }

        _ = _dialogs.Form<bool>(
            new FormDialogOptions(
                "Replace",
                PreferredWidth: Width,
                MinWidth: 40,
                MinHeight: MinimumHeight)
            {
                InitialFocus = pattern,
                Movable = true,
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
                    return FormDialogOutcome<bool>.Complete(false);

                if (formEvent.IsValueChanged)
                {
                    error = null;
                    if (!formEvent.IsValueChangedFrom(replacement))
                    {
                        SetButtonState(canReplace: false, showFindNext: false);
                        invalidatePreview();
                    }

                    return FormDialogOutcome<bool>.Continue();
                }

                if (!formEvent.IsSubmitted)
                    return FormDialogOutcome<bool>.Continue();

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
                {
                    SetButtonState(canReplace: false, showFindNext: false);
                    invalidatePreview();
                    return FormDialogOutcome<bool>.ContinueWithFocus(pattern);
                }

                string command = formEvent.Command ?? "find";
                if (string.Equals(command, "replace", StringComparison.Ordinal))
                {
                    if (!replaceEnabled)
                        return FormDialogOutcome<bool>.Continue();

                    replacement.AcceptHistory();
                    EditorReplaceCommandResult result = replace(search, replacement.Text);
                    SetButtonState(
                        canReplace: result.HasMatch,
                        showFindNext: result.IsSuccess || findNext);
                    error = result.ErrorMessage;
                    return FormDialogOutcome<bool>.Continue();
                }

                if (string.Equals(command, "replace-all", StringComparison.Ordinal))
                {
                    pattern.AcceptHistory();
                    replacement.AcceptHistory();
                    EditorReplaceCommandResult result = replaceAll(search, replacement.Text);
                    SetButtonState(canReplace: false, showFindNext: false);
                    invalidatePreview();
                    if (result.IsSuccess)
                        return FormDialogOutcome<bool>.Complete(true);

                    error = result.ErrorMessage ?? "Text not found.";
                    return FormDialogOutcome<bool>.Continue();
                }

                pattern.AcceptHistory();
                EditorReplaceCommandResult findResult = find(search);
                bool showFindNext = findResult.HasMatch || findNext && findResult.IsSuccess;
                SetButtonState(findResult.HasMatch, showFindNext);
                if (!findResult.IsSuccess)
                    invalidatePreview();
                error = findResult.ErrorMessage;
                return FormDialogOutcome<bool>.Continue();
            });
    }

    private static IReadOnlyList<DialogButton> CreateButtons(bool replaceEnabled, bool findNext) =>
    [
        DialogButton.Default("find", findNext ? "Find next" : "Find", 'F'),
        new DialogButton("replace", "Replace", 'R', IsEnabled: replaceEnabled),
        DialogButton.Action("replace-all", "Replace all", 'A'),
        DialogButton.Cancel(),
    ];
}
