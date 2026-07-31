namespace CSharpFar.Ui;

public sealed class ModuleInputDialog
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly FormFieldFactory _fields;

    public ModuleInputDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public string? Show(string title, string prompt, string? initialText)
    {
        var result = new SingleLineInputDialog(_modalDialogs, _fields).Show(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = true,
        });

        return result.IsConfirmed ? result.Text : null;
    }
}
