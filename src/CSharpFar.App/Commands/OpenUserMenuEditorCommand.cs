using System.Security;
using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenUserMenuEditorCommand : IApplicationCommand
{
    private readonly bool _local;

    public OpenUserMenuEditorCommand(bool local = false) => _local = local;

    public string CommandId => _local
        ? MenuCommandIds.SettingsOpenLocalUserMenu
        : MenuCommandIds.SettingsOpenUserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        !_local || context.CanUseLocalUserMenu(context.ResolvePanelTarget(args));

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
            return ApplicationCommandResult.Rendered();

        string? path = _local
            ? Path.Combine(context.ResolvePanelTarget(args).State.CurrentDirectory, UserMenuResolver.LocalFileName)
            : null;

        UserMenuItem[] workingCopy;
        try
        {
            workingCopy = _local
                ? UserMenuFile.ReadOrNull(path!) ?? []
                : UserMenuItemRules.CloneItems(context.UserMenu.Items);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or SecurityException)
        {
            context.Dialogs.Message("User menu", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        bool pendingChanges = false;
        while (true)
        {
            string title = _local ? "Local user menu — " + path : "User menu";
            UserMenuEditorDialogResult result = new UserMenuEditorDialog(context.Dialogs, context.Fields)
                .Show(workingCopy, title);
            if (!result.Changed && !pendingChanges)
                return ApplicationCommandResult.Rendered();

            workingCopy = UserMenuItemRules.CloneItems(result.Items);
            pendingChanges = true;

            try
            {
                if (_local)
                    UserMenuFile.WriteSafely(path!, UserMenuItemRules.NormalizeItems(workingCopy));
                else
                    context.UserMenu.Save(workingCopy);
                return ApplicationCommandResult.Rendered();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException)
            {
                int choice = context.Dialogs.Message(
                    "User Menu",
                    "Cannot save user menu:\n" + ex.Message,
                    ["Edit", "Discard"]);
                if (choice != 0)
                    return ApplicationCommandResult.Rendered();
            }
        }
    }
}
