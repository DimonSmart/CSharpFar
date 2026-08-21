using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class OperationCancelDialog
{
    private const string YesButton = "yes";

    private readonly DialogService _dialogs;

    public OperationCancelDialog(DialogService dialogs) =>
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    public bool Show(
        string interruptedMessage = "Operation has been interrupted",
        string confirmationMessage = "Do you really want to cancel it?")
    {
        var actions = FormControls.Buttons(
        [
            DialogButton.Default(YesButton, "Yes", 'Y'),
            DialogButton.Action("no", "No", 'N'),
        ]);

        return _dialogs.Form(
            new FormDialogOptions("", PreferredWidth: 46, PreferredHeight: 8)
            {
                Appearance = DialogAppearance.Standard,
                InitialFocus = actions,
            },
            rows: () =>
            [
                FormControls.Label(interruptedMessage, TextAlignment.Center),
                FormControls.Label(confirmationMessage, TextAlignment.Center),
            ],
            footer: () => [actions],
            handle: dialogEvent => dialogEvent.IsCancelled
                ? FormDialogOutcome<bool>.Complete(false)
                : dialogEvent.Command is not null
                    ? FormDialogOutcome<bool>.Complete(dialogEvent.Command == YesButton)
                    : FormDialogOutcome<bool>.Continue());
    }
}
