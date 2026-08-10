using CSharpFar.Core.Models;

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

public readonly record struct SingleLineInputDialogResult(bool IsConfirmed, string Text);

public sealed class SingleLineInputDialog
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

    public SingleLineInputDialogResult Show(SingleLineInputDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return RunLoop(options);
    }

    private SingleLineInputDialogResult RunLoop(SingleLineInputDialogOptions options)
    {
        TextField field = _fields.Text(new TextFieldOptions(
            options.InitialText,
            options.History,
            options.MaskInput,
            SubmitOnEnter: true));
        string? error = null;
        var actions = FormControls.OkCancel();

        return _forms.Show(
            new FormDialogOptions(options.Title, DialogWidth, DialogHeight, MinWidth: 20, MinHeight: 5)
            {
                DoubleBorder = false,
                Appearance = FormDialogAppearance.Popup,
            },
            rows: () => [
                FormControls.Label(options.Prompt),
                FormControls.Text(field),
                FormControls.Spacer(),
            ],
            footer: () => FormFooter.ErrorAndButtons(() => error, actions),
            handle: result =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<SingleLineInputDialogResult>.Complete(new(false, string.Empty));

                if (!result.IsSubmitted)
                    return FormDialogOutcome<SingleLineInputDialogResult>.Continue();

                string text = field.TrimmedText;
                error = text.Length == 0 && !options.AllowEmpty
                    ? "A value is required."
                    : options.Validate?.Invoke(text);
                if (error is not null)
                    return FormDialogOutcome<SingleLineInputDialogResult>.ContinueWithFocus(field);

                field.AcceptHistory();
                return FormDialogOutcome<SingleLineInputDialogResult>.Complete(new(true, text));
            });
    }
}
