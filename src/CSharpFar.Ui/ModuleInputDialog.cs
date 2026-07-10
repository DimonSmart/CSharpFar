namespace CSharpFar.Ui;

public sealed class ModuleInputDialog
{
    private readonly ModalDialogHost _modalDialogs;

    public ModuleInputDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public string? Show(string title, string prompt, string? initialText)
    {
        var result = new SingleLineInputDialog(_modalDialogs).Show(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = true,
        });

        return result.IsConfirmed ? result.Text : null;
    }
}
