using System.Security;
using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenUserMenuEditorCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenUserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        UserMenuItem[] workingCopy = UserMenuItemRules.CloneItems(context.UserMenu.Items);
        bool pendingChanges = false;

        while (true)
        {
            UserMenuEditorDialogResult result = new UserMenuEditorDialog(context.Dialogs, context.Fields)
                .Show(workingCopy);
            if (!result.Changed && !pendingChanges)
                return ApplicationCommandResult.Rendered();

            workingCopy = UserMenuItemRules.CloneItems(result.Items);
            pendingChanges = true;

            try
            {
                context.UserMenu.Save(workingCopy);
                return ApplicationCommandResult.Rendered();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                int choice = context.Dialogs.Message(
                    "User Menu",
                    $"Cannot save user menu:\n{ex.Message}",
                    ["Edit", "Discard"]);
                if (choice != 0)
                    return ApplicationCommandResult.Rendered();
            }
        }
    }

}
