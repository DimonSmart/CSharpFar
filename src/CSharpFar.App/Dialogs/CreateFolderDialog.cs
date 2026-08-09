using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 9;
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly FormFieldFactory _fields;

    private readonly FormDialogs _forms;

    public CreateFolderDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _forms = new FormDialogs(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        var fields = _fields;
        TextField folderName = fields.Text("folder-name", initialText ?? string.Empty,
            AppTextHistoryIds.CreateFolderName, submitOnEnter: true);
        var actions = FormControls.Buttons(
            "actions",
            DialogButton.Default("ok", "OK", 'O'),
            DialogButton.Cancel());
        string? error = null;

        return _forms.Show(
            new FormDialogOptions(Title, DialogWidth, DialogHeight, MinWidth: 40),
            rows: () =>
            [
                FormControls.Label(Prompt),
                FormControls.Text(folderName),
                FormControls.Separator(),
            ],
            footer: () => [FormControls.Error(() => error), actions],
            handle: result =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<string?>.Complete(null);

                if (result.IsValueChanged)
                    error = null;

                if (result.IsSubmitted)
                {
                    string? accepted = TrySubmit(folderName, validate, ref error);
                    if (accepted is not null)
                        return FormDialogOutcome<string?>.Complete(accepted);

                    return FormDialogOutcome<string?>.ContinueWithFocus(folderName.Id);
                }

                return FormDialogOutcome<string?>.Continue();
            });
    }

    private static string? TrySubmit(
        TextField folderName,
        Func<string, string?>? validate,
        ref string? error)
    {
        string text = folderName.TrimmedText;
        if (text.Length == 0)
            return null;

        error = validate?.Invoke(text);
        if (error is not null)
            return null;

        folderName.AcceptHistory();
        return text;
    }

}
