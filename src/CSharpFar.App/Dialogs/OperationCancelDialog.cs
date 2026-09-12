using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class OperationCancelDialog
{
    private readonly DialogService _dialogs;

    public OperationCancelDialog(DialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public bool Show(
        string interruptedMessage = "Operation has been interrupted",
        string confirmationMessage = "Do you really want to cancel it?")
    {
        ChoiceDialogResult result = _dialogs.Choice(new ChoiceDialogOptions
        {
            Lines = [interruptedMessage, confirmationMessage],
            Buttons =
            [
                DialogButton.Default("yes", "Yes", 'Y'),
                DialogButton.Cancel("No", 'N', "no"),
            ],
            DefaultButtonIndex = 0,
            CancelButtonIndex = 1,
        });

        return result.ButtonId == "yes";
    }
}
