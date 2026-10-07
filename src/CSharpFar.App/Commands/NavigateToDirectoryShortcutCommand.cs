using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

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
            NavigateToDirectoryShortcutArgs menu =>
                context.IsPanelsMode &&
                context.CanAccessLocalFileSystem(context.ActiveState) &&
                DirectoryShortcutNormalizer.IsValidNumber(menu.Number),
            NavigateToCommittedDirectoryShortcutArgs committed =>
                context.CanAccessLocalFileSystem(context.GetPanelState(committed.Side)) &&
                DirectoryShortcutNormalizer.IsValidNumber(committed.Number),
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

        FilePanelState state = context.GetPanelState(side);
        if (!context.CanAccessLocalFileSystem(state))
            return ApplicationCommandResult.Rendered();

        if (!LocalPathNormalizer.Current.TryNormalize(
            path,
            basePath: null,
            out string canonicalPath))
        {
            context.Dialogs.Message("Directory Shortcut", $"Invalid directory path: {path}");
            return ApplicationCommandResult.Rendered();
        }

        if (!Directory.Exists(canonicalPath))
        {
            context.Dialogs.Message("Directory Shortcut", $"Directory not found: {canonicalPath}");
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            context.ResetTransientNavigationUi();
            context.Controller.LoadDirectory(state, canonicalPath, context.PanelOptions);
            context.StartWatching(state, side);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Dialogs.Message("Directory Shortcut", ex.Message);
        }

        return ApplicationCommandResult.Rendered();
    }
}
