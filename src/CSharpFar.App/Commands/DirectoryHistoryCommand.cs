using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Ui;

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
            var result = context.Dialogs.Select(new SelectionDialogOptions<DirectoryHistoryItem>
            {
                Title = "Directory History",
                Items = context.History.GetDirectoryHistory().Reverse().ToArray(),
                ItemText = static item => item.Path,
                Presentation = SelectionDialogPresentation.Standard,
            });
            string? path = result.IsConfirmed ? result.SelectedItem?.Path : null;
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!LocalPathNormalizer.Current.TryNormalize(
                path,
                basePath: null,
                out string canonicalPath))
            {
                context.Dialogs.Message("Directory History", $"Invalid directory path: {path}");
                return ApplicationCommandResult.Rendered();
            }

            if (!Directory.Exists(canonicalPath))
            {
                context.Dialogs.Message("Directory History", $"Directory not found: {canonicalPath}");
                return ApplicationCommandResult.Rendered();
            }

            try
            {
                context.Controller.LoadDirectory(target.State, canonicalPath, context.PanelOptions);
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
