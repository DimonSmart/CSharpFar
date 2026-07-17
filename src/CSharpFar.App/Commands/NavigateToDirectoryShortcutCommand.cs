using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToDirectoryShortcutArgs(int Number);

internal sealed record NavigateToCommittedDirectoryShortcutArgs(
    int Number,
    string Path,
    PanelSide Side);

internal sealed class NavigateToDirectoryShortcutCommand : IApplicationCommand
{
    public string CommandId => DirectoryShortcutCommandIds.Navigate;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args switch
        {
            NavigateToDirectoryShortcutArgs menu => context.IsPanelsMode && DirectoryShortcutNormalizer.IsValidNumber(menu.Number),
            NavigateToCommittedDirectoryShortcutArgs committed => DirectoryShortcutNormalizer.IsValidNumber(committed.Number),
            _ => false,
        };

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
            return ApplicationCommandResult.Rendered();

        (string? path, PanelSide side) = args switch
        {
            NavigateToDirectoryShortcutArgs menu => (
                DirectoryShortcutNormalizer.Normalize(context.Settings.DirectoryShortcuts)
                    .SingleOrDefault(candidate => candidate.Number == menu.Number)?.Path,
                context.ActiveSide),
            NavigateToCommittedDirectoryShortcutArgs committed => (committed.Path, committed.Side),
            _ => throw new InvalidOperationException(),
        };

        if (path is null)
            return ApplicationCommandResult.Rendered();

        if (!Directory.Exists(path))
        {
            new MessageDialog(context.ModalDialogs)
                .Show("Directory Shortcut", $"Directory not found: {path}");
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            FilePanelState state = context.GetPanelState(side);
            context.ResetTransientNavigationUi();
            context.Controller.LoadDirectory(state, path, context.PanelOptions);
            context.StartWatching(state, side);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(context.ModalDialogs).Show("Directory Shortcut", ex.Message);
        }

        return ApplicationCommandResult.Rendered();
    }
}
