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

        if (context.UserMenu.Items.Count == 0)
        {
            context.Dialogs.Message(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return ApplicationCommandResult.Rendered();
        }

        var result = context.Dialogs.Select(new SelectionDialogOptions<UserMenuItem>
        {
            Title = "User Menu",
            Items = context.UserMenu.Items,
            ItemText = static item => item.Title,
        });
        string? command = result.IsConfirmed ? result.SelectedItem?.Command : null;
        if (command is null)
            return ApplicationCommandResult.Rendered();

        string expanded = operands.Expand(command);

        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }
}
