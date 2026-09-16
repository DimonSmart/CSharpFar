using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class UserMenuItemEditDialog
{
    private const int DialogWidth = 64;
    private const int DialogHeight = 17;

    private static readonly PlatformKind?[] PlatformValues =
    [
        null,
        PlatformKind.Windows,
        PlatformKind.MacOs,
        PlatformKind.Linux,
    ];

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
        CompactChoiceFormRow<PlatformKind?> platformRow = FormControls.CompactChoice(
            "Platform",
            PlatformValues,
            FormatPlatform,
            currentItem?.Platform);
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
                platformRow,
                FormControls.Spacer(),
                FormControls.Label("Available placeholders:"),
                FormControls.Label("{current} {selected} {panelDir}"),
                FormControls.Label("{otherPanelDir}"),
            ],
            footer: () => [actions],
            submit: () => Validate(title, command, platformRow));
    }

    private static FormSubmitResult<UserMenuItem> Validate(
        TextField title,
        TextField command,
        CompactChoiceFormRow<PlatformKind?> platform)
    {
        if (string.IsNullOrWhiteSpace(title.Text))
            return FormSubmit.Invalid<UserMenuItem>("Title is required.", title);
        if (string.IsNullOrWhiteSpace(command.Text))
            return FormSubmit.Invalid<UserMenuItem>("Command is required.", command);

        return FormSubmit.Success(new UserMenuItem
        {
            Title = title.Text.Trim(),
            Command = command.Text,
            Platform = platform.Value,
        });
    }

    private static string FormatPlatform(PlatformKind? platform) => platform switch
    {
        null => "All platforms",
        PlatformKind.Windows => "Windows",
        PlatformKind.MacOs => "macOS",
        PlatformKind.Linux => "Linux",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };
}
