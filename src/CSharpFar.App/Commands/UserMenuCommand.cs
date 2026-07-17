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
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }
        if (context.UserMenu.Items.Count == 0)
        {
            new MessageDialog(context.ModalDialogs).Show(
                "User Menu", "User menu is empty.\nEdit user-menu.json to add commands.");
            return ApplicationCommandResult.Rendered();
        }

        string? command = new UserMenuDialog(context.ModalDialogs).Show(context.UserMenu.Items);
        if (command is null)
            return ApplicationCommandResult.Rendered();

        FilePanelItem? item = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedItem) ? resolvedItem : null;
        string currentFile = item is { IsParentDirectory: false } ? item.FullPath : string.Empty;

        IReadOnlyList<string> selected = target.State.SelectedPaths.Count > 0
            ? [.. target.State.SelectedPaths]
            : [];

        string currentDirectory = target.ActiveCommitted?.CurrentDirectory ?? target.State.CurrentDirectory;
        string otherDirectory = target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory;
        string expanded = PlaceholderExpander.Expand(
            command,
            currentFile,
            selected,
            currentDirectory,
            otherDirectory);

        context.ExecuteCommand(expanded);
        return ApplicationCommandResult.Rendered();
    }
}
