namespace CSharpFar.Ui;

public sealed class ModuleInputDialog
{
    private readonly ModalDialogHost _modalDialogs;
    private readonly ITextFieldHistoryProvider _history;

    public ModuleInputDialog(ModalDialogHost modalDialogs, ITextFieldHistoryProvider history)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public string? Show(string title, string prompt, string? initialText)
    {
        var result = new SingleLineInputDialog(_modalDialogs, _history).Show(new SingleLineInputDialogOptions
        {
            Title = title,
            Prompt = prompt,
            InitialText = initialText ?? string.Empty,
            AllowEmpty = true,
        });

        return result.IsConfirmed ? result.Text : null;
    }
}
