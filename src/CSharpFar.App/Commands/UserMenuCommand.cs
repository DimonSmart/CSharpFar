using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class UserMenuCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.UserMenu;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (context.UserMenu.Items.Count == 0)
        {
            new MessageDialog(context.ModalDialogs).Show(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return ApplicationCommandResult.Rendered();
        }

        string? command = new UserMenuDialog(context.ModalDialogs).Show(context.UserMenu.Items);
        if (command is null)
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = target.Committed is { } committed
            ? ApplicationCommandContext.TryResolveCommittedCurrentItem(target.State, committed, out var committedItem)
                ? committedItem
                : null
            : context.Controller.CurrentItem(target.State);
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;

        IReadOnlyList<string> selected = target.State.SelectedPaths.Count > 0
            ? [.. target.State.SelectedPaths]
            : [];

        string otherDirectory = target.PassiveState.CurrentDirectory;
        string expanded = PlaceholderExpander.Expand(
            command,
            currentFile,
            selected,
            target.State.CurrentDirectory,
            otherDirectory);

        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }
}
