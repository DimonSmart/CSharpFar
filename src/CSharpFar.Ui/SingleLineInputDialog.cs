
namespace CSharpFar.Ui;

public sealed class SingleLineInputDialogOptions
{
    public string Title { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string InitialText { get; init; } = string.Empty;
    public bool AllowEmpty { get; init; }
    public bool MaskInput { get; init; }
    public TextHistoryId? History { get; init; }
    public Func<string, string?>? Validate { get; init; }
}

internal sealed class SingleLineInputDialog
{
    private const int DialogWidth = 52;
    private const int DialogHeight = 7;

    private readonly FormDialogs _forms;
    private readonly FormFieldFactory _fields;

    public SingleLineInputDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _forms = new FormDialogs(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public string? Show(SingleLineInputDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        TextField field = _fields.Text(new TextFieldOptions(
            options.InitialText,
            options.History,
            options.MaskInput,
            SubmitOnEnter: true));
        var actions = FormControls.OkCancel();

        return _forms.Show(
            new FormDialogOptions(
                options.Title,
                PreferredWidth: DialogWidth,
                PreferredHeight: DialogHeight,
                MinWidth: 20,
                MinHeight: 5),
            rows: () =>
            [
                FormControls.Label(options.Prompt),
                FormControls.Text(field),
                FormControls.Spacer(),
            ],
            footer: () => [actions],
            submit: () =>
            {
                string text = field.TrimmedText;
                if (text.Length == 0 && !options.AllowEmpty)
                    return FormSubmit.Invalid<string>("A value is required.", field);

                string? error = options.Validate?.Invoke(text);
                return error is null
                    ? FormSubmit.Success(text)
                    : FormSubmit.Invalid<string>(error, field);
            });
    }
}
