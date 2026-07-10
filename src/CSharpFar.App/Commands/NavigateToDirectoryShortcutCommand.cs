using CSharpFar.App.Dialogs;
using CSharpFar.App.DirectoryShortcuts;

namespace CSharpFar.App.Commands;

internal sealed record NavigateToDirectoryShortcutArgs(int Number);

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

        int number = ((NavigateToDirectoryShortcutArgs)args!).Number;
        var item = DirectoryShortcutNormalizer.Normalize(context.Settings.DirectoryShortcuts)
            .SingleOrDefault(candidate => candidate.Number == number);
        if (item is null)
            return ApplicationCommandResult.Rendered();

        if (!Directory.Exists(item.Path))
        {
            new MessageDialog(context.ModalDialogs)
                .Show("Directory Shortcut", $"Directory not found: {item.Path}");
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            context.ResetTransientNavigationUi();
            context.Controller.LoadDirectory(context.ActiveState, item.Path, context.PanelOptions);
            context.StartWatching(context.ActiveState, context.ActiveSide);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(context.ModalDialogs).Show("Directory Shortcut", ex.Message);
        }

        return ApplicationCommandResult.Rendered();
    }
}
