using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToDirectoryShortcutArgs(
    int Number,
    string? CommittedPath = null,
    PanelSide? Side = null);

internal sealed class NavigateToDirectoryShortcutCommand : IApplicationCommand
{
    public string CommandId => DirectoryShortcutCommandIds.Navigate;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is NavigateToDirectoryShortcutArgs shortcutArgs &&
        (IsCommittedInvocation(shortcutArgs) || context.IsPanelsMode) &&
        DirectoryShortcutNormalizer.IsValidNumber(shortcutArgs.Number);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
            return ApplicationCommandResult.Rendered();

        var shortcutArgs = (NavigateToDirectoryShortcutArgs)args!;
        string? path = shortcutArgs.CommittedPath;
        if (path is null)
        {
            var item = DirectoryShortcutNormalizer.Normalize(context.Settings.DirectoryShortcuts)
                .SingleOrDefault(candidate => candidate.Number == shortcutArgs.Number);
            path = item?.Path;
        }

        if (path is null || (IsCommittedInvocation(shortcutArgs) && shortcutArgs.Side is null))
            return ApplicationCommandResult.Rendered();

        if (!Directory.Exists(path))
        {
            new MessageDialog(context.ModalDialogs)
                .Show("Directory Shortcut", $"Directory not found: {path}");
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            PanelSide side = shortcutArgs.Side ?? context.ActiveSide;
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

    private static bool IsCommittedInvocation(NavigateToDirectoryShortcutArgs args) =>
        args.CommittedPath is not null || args.Side is not null;
}
