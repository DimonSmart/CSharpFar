using System.Security;
using CSharpFar.App.Dialogs;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenUserMenuEditorCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenUserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        UserMenuItem[] workingCopy = CloneItems(context.UserMenu.Items);
        bool pendingChanges = false;

        while (true)
        {
            UserMenuEditorDialogResult result = new UserMenuEditorDialog(context.Dialogs, context.Fields)
                .Show(workingCopy);
            if (!result.Changed && !pendingChanges)
                return ApplicationCommandResult.Rendered();

            workingCopy = CloneItems(result.Items);
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

    private static UserMenuItem[] CloneItems(IEnumerable<UserMenuItem> items) =>
        items.Select(item => new UserMenuItem
        {
            Title = item.Title,
            Command = item.Command,
        }).ToArray();
}
