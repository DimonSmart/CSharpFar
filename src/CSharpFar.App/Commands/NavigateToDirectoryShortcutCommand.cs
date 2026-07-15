using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToDirectoryShortcutArgs(int Number, string? CommittedPath = null);

internal sealed class NavigateToDirectoryShortcutCommand : IApplicationCommand
{
    public string CommandId => DirectoryShortcutCommandIds.Navigate;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasVisiblePanels &&
        args is NavigateToDirectoryShortcutArgs shortcutArgs &&
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
            context.ResetTransientNavigationUi();
            context.Controller.LoadDirectory(context.ActiveState, path, context.PanelOptions);
            context.StartWatching(context.ActiveState, context.ActiveSide);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(context.ModalDialogs).Show("Directory Shortcut", ex.Message);
        }

        return ApplicationCommandResult.Rendered();
    }
}
