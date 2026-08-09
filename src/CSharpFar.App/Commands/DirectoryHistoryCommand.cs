using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class DirectoryHistoryCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.DirectoryHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!context.CanAccessLocalFileSystem(target.State))
            return ApplicationCommandResult.Rendered();

        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            string? path = new DirectoryHistoryDialog(context.ModalDialogs)
                .Show(context.History.GetDirectoryHistory());
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!Directory.Exists(path))
            {
                context.Dialogs.Message("Directory History", $"Directory not found: {path}");
                return ApplicationCommandResult.Rendered();
            }

            try
            {
                context.Controller.LoadDirectory(target.State, path, context.PanelOptions);
                context.StartWatching(target.State, target.Side);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.Dialogs.Message("Directory History", ex.Message);
            }

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }
}
