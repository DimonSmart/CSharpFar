using CSharpFar.App.FunctionKeys;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class UserMenuCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.UserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!PanelCommandUserMenuOperands.TryCreate(target, context, out var operands))
            return ApplicationCommandResult.Rendered();

        PlatformKind platform = context.UserMenu.RuntimePlatform
            ?? throw new InvalidOperationException("User menu runtime platform is not configured.");
        UserMenuItem[] items = UserMenuAvailability.FilterForPlatform(context.UserMenu.Items, platform);
        if (items.Length == 0)
        {
            context.Dialogs.Message(
                "User Menu", "User menu is empty.\nConfigure it in Options → User menu...");
            return ApplicationCommandResult.Rendered();
        }

        var result = context.Dialogs.Select(new SelectionDialogOptions<UserMenuItem>
        {
            Title = "User Menu",
            Items = items,
            ItemText = static item => item.Title,
            Presentation = SelectionDialogPresentation.Standard,
            Appearance = DialogAppearance.Standard,
            DoubleBorder = true,
        });
        UserMenuItem? item = result.IsConfirmed ? result.SelectedItem : null;
        if (item is null)
            return ApplicationCommandResult.Rendered();

        string expanded = operands.Expand(UserMenuItemRules.EffectiveCommand(item));

        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }
}
