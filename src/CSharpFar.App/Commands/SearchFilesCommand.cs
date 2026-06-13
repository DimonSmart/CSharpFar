using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SearchFilesCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Search;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.ActiveState.SourceId == PanelSourceId.Local;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
        {
            new MessageDialog(context.Screen).Show(
                "Search",
                "Search is only supported for local panels.");
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            var request = new SearchDialog(context.Screen).Show(context.ActiveState.CurrentDirectory);
            if (request is null)
                return ApplicationCommandResult.Rendered();

            SearchRunResult result;
            try
            {
                result = new SearchProgressDialog(context.Screen, context.SearchService, context.Palette).Show(request);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
            {
                new MessageDialog(context.Screen).Show("Search", ex.Message);
                return ApplicationCommandResult.Rendered();
            }

            if (result.GoToResult is not null)
            {
                context.GoToSearchResult(context.ActiveState, context.ActiveSide, result.GoToResult);
                return ApplicationCommandResult.Rendered();
            }

            if (result.DiscardResults)
                return ApplicationCommandResult.Rendered();

            if (result.Results.Count == 0)
            {
                string message = result.Cancelled ? "Search cancelled. No files found." : "No files found.";
                new MessageDialog(context.Screen).Show("Search", message);
                return ApplicationCommandResult.Rendered();
            }

            context.OpenSearchResultsPanel(context.ActiveState, request, result.Results, result.Cancelled);
            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }
}
