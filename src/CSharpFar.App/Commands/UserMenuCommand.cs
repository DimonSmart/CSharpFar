using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.UserMenu;

namespace CSharpFar.App.Commands;

internal sealed class UserMenuCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.UserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (context.UserMenu.Items.Count == 0)
        {
            new MessageDialog(context.Screen, context.Palette).Show(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return ApplicationCommandResult.Rendered();
        }

        string? command = new UserMenuDialog(context.Screen, context.Palette).Show(context.UserMenu.Items);
        if (command is null)
            return ApplicationCommandResult.Rendered();

        var item = context.Controller.CurrentItem(context.ActiveState);
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;

        IReadOnlyList<string> selected = context.ActiveState.SelectedPaths.Count > 0
            ? [.. context.ActiveState.SelectedPaths]
            : [];

        string otherDirectory = context.PassiveState.CurrentDirectory;
        string expanded = PlaceholderExpander.Expand(
            command,
            currentFile,
            selected,
            context.ActiveState.CurrentDirectory,
            otherDirectory);

        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }
}
