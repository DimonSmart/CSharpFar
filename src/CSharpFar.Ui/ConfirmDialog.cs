namespace CSharpFar.Ui;

/// <summary>Asks the user to confirm a destructive action. Returns true if confirmed.</summary>
internal sealed class ConfirmDialog
{
    private readonly ChoiceDialog _choice;

    public ConfirmDialog(ModalDialogHost modalDialogs) =>
        _choice = new ChoiceDialog(modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs)));

    public bool Show(string title, string question, string itemName)
    {
        ChoiceDialogResult result = _choice.Show(new ChoiceDialogOptions
        {
            Title = title,
            Lines = [question, itemName],
            Buttons =
            [
                DialogButton.Default("ok", "OK", 'O'),
                DialogButton.Cancel("Cancel", 'C'),
            ],
            DefaultButtonIndex = 0,
            CancelButtonIndex = 1,
        });

        return result.ButtonId == "ok";
    }
}
