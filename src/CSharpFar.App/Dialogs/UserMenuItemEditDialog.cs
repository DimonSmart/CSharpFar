using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class UserMenuItemEditDialog
{
    private const int DialogWidth = 64;
    private const int DialogHeight = 15;

    private readonly DialogService _dialogs;
    private readonly FormFieldFactory _fields;

    public UserMenuItemEditDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public UserMenuItem? Show(UserMenuItem? currentItem)
    {
        TextField title = _fields.Text(new TextFieldOptions(currentItem?.Title ?? string.Empty));
        TextField command = _fields.Text(new TextFieldOptions(currentItem?.Command ?? string.Empty));
        TextInputRow titleRow = FormControls.Text(title);
        TextInputRow commandRow = FormControls.Text(command);
        var actions = FormControls.OkCancel();

        return _dialogs.Form(
            new FormDialogOptions("User menu item", DialogWidth, DialogHeight),
            rows: () =>
            [
                FormControls.Label("Title"),
                titleRow,
                FormControls.Spacer(),
                FormControls.Label("Command"),
                commandRow,
                FormControls.Spacer(),
                FormControls.Label("Available placeholders:"),
                FormControls.Label("{current} {selected} {panelDir}"),
                FormControls.Label("{otherPanelDir}"),
            ],
            footer: () => [actions],
            submit: () => Validate(title, command));
    }

    private static FormSubmitResult<UserMenuItem> Validate(TextField title, TextField command)
    {
        if (string.IsNullOrWhiteSpace(title.Text))
            return FormSubmit.Invalid<UserMenuItem>("Title is required.", title);
        if (string.IsNullOrWhiteSpace(command.Text))
            return FormSubmit.Invalid<UserMenuItem>("Command is required.", command);

        return FormSubmit.Success(new UserMenuItem
        {
            Title = title.Text.Trim(),
            Command = command.Text,
        });
    }
}
