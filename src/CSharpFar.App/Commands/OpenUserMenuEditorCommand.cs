using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using System.Security;
using CSharpFar.App.Dialogs;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenUserMenuEditorCommand(PanelCommandTargetResolver targetResolver, FilePanelSourceRegistry sourceRegistry, UserMenuStore userMenu, DialogService dialogs, FormFieldFactory fields, bool local = false) : IApplicationCommand
{
    public string CommandId => local
        ? MenuCommandIds.SettingsOpenLocalUserMenu
        : MenuCommandIds.SettingsOpenUserMenu;

    public bool CanExecute(object? args = null) =>
        !local || PanelCommandUserMenuOperands.CanUseLocalUserMenu(sourceRegistry, targetResolver.ResolvePanelTarget(args));

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (!CanExecute(args))
            return ApplicationCommandResult.Rendered();

        string? path = local
            ? Path.Combine(targetResolver.ResolvePanelTarget(args).State.CurrentDirectory, UserMenuResolver.LocalFileName)
            : null;

        UserMenuItem[] workingCopy;
        try
        {
            workingCopy = local
                ? UserMenuFile.ReadOrNull(path!) ?? []
                : UserMenuItemRules.CloneItems(userMenu.Items);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or SecurityException)
        {
            dialogs.Message("User menu", ex.Message);
            return ApplicationCommandResult.Rendered();
        }

        bool pendingChanges = false;
        while (true)
        {
            string title = local ? "Local user menu — " + path : "User menu";
            UserMenuEditorDialogResult result = new UserMenuEditorDialog(dialogs, fields)
                .Show(workingCopy, title);
            if (!result.Changed && !pendingChanges)
                return ApplicationCommandResult.Rendered();

            workingCopy = UserMenuItemRules.CloneItems(result.Items);
            pendingChanges = true;

            try
            {
                if (local)
                    UserMenuFile.WriteSafely(path!, UserMenuItemRules.NormalizeItems(workingCopy));
                else
                    userMenu.Save(workingCopy);
                return ApplicationCommandResult.Rendered();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidDataException)
            {
                int choice = dialogs.Message(
                    "User Menu",
                    "Cannot save user menu:\n" + ex.Message,
                    ["Edit", "Discard"]);
                if (choice != 0)
                    return ApplicationCommandResult.Rendered();
            }
        }
    }
}
