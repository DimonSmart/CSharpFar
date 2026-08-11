using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly FormFieldFactory _fields;
    private readonly DialogService _dialogs;

    public CreateFolderDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        TextField folderName = _fields.Text(new TextFieldOptions(
            initialText ?? string.Empty,
            AppTextHistoryIds.CreateFolderName,
            SubmitOnEnter: true));
        var actions = FormControls.OkCancel();
        return _dialogs.Form(
            new FormDialogOptions(Title, MinWidth: 40),
            rows: () =>
            [
                FormControls.Label(Prompt),
                FormControls.Text(folderName),
                FormControls.Separator(),
            ],
            footer: () => [actions],
            submit: () =>
            {
                string text = folderName.TrimmedText;
                if (text.Length == 0)
                    return FormSubmit.Invalid<string>("A folder name is required.", folderName);

                string? error = validate?.Invoke(text);
                return error is null
                    ? FormSubmit.Success(text)
                    : FormSubmit.Invalid<string>(error, folderName);
            });
    }
}
