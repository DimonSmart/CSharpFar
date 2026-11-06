using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class UserMenuItemEditDialog
{
    private const int DialogWidth = 64;
    private const int DialogHeight = 20;

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
        TextField id = _fields.Text(new TextFieldOptions(currentItem?.Id ?? string.Empty));
        TextInputRow titleRow = FormControls.Text(title);
        TextInputRow commandRow = FormControls.Text(command);
        TextInputRow idRow = FormControls.Text(id);
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
                FormControls.Label("Command (optional; empty = Title)"),
                commandRow,
                FormControls.Spacer(),
                FormControls.Label("ID (optional; overrides inherited item)"),
                idRow,
                FormControls.Spacer(),
                platformRow,
                FormControls.Spacer(),
                FormControls.Label("Available placeholders:"),
                FormControls.Label("{current} {selected} {panelDir}"),
                FormControls.Label("{otherPanelDir} {menuDir}"),
            ],
            footer: () => [actions],
            submit: () => Validate(title, command, id, platformRow));
    }

    private static FormSubmitResult<UserMenuItem> Validate(
        TextField title,
        TextField command,
        TextField id,
        CompactChoiceFormRow<PlatformKind?> platform)
    {
        if (string.IsNullOrWhiteSpace(title.Text))
            return FormSubmit.Invalid<UserMenuItem>("Title is required.", title);

        string normalizedId = id.Text.Trim();
        if (normalizedId.Length > 0 && !UserMenuItemRules.IsValidId(normalizedId))
            return FormSubmit.Invalid<UserMenuItem>("ID must start with a-z and contain 1-64 lowercase letters, digits, '.', '_' or '-'.", id);

        string normalizedTitle = title.Text.Trim();
        return FormSubmit.Success(new UserMenuItem
        {
            Title = normalizedTitle,
            Id = normalizedId.Length == 0 ? null : normalizedId,
            Command = UserMenuItemRules.NormalizeCommand(normalizedTitle, command.Text),
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
