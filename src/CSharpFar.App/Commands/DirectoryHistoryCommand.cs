using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class DirectoryHistoryCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.DirectoryHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        try
        {
            string? path = new DirectoryHistoryDialog(context.Screen, context.Palette)
                .Show(context.History.GetDirectoryHistory());
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!Directory.Exists(path))
            {
                new MessageDialog(context.Screen).Show("Directory History", $"Directory not found: {path}");
                return ApplicationCommandResult.Rendered();
            }

            try
            {
                context.Controller.LoadDirectory(context.ActiveState, path, context.PanelOptions);
                context.StartWatching(context.ActiveState, context.ActiveSide);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                new MessageDialog(context.Screen).Show("Directory History", ex.Message);
            }

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }
}
