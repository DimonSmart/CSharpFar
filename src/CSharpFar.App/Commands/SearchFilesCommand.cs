using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SearchFilesCommand(PanelCommandTargetResolver targetResolver, FilePanelSourceRegistry sourceRegistry, DialogService dialogs, FormFieldFactory fields, ISearchService searchService, PanelSearchResultsService searchResults, Action resetFunctionKeyLayer) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Search;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, targetResolver.ResolvePanelTarget(args).State);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            dialogs.Message(
                "Search",
                "Search is only supported for local panels.");
            return ApplicationCommandResult.Rendered();
        }

        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
        {
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            var request = new SearchDialog(dialogs, fields).Show(target.State.CurrentDirectory);
            if (request is null)
                return ApplicationCommandResult.Rendered();

            SearchRunResult result;
            try
            {
                result = new SearchProgressDialog(searchService, dialogs).Show(request);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
            {
                dialogs.Message("Search", ex.Message);
                return ApplicationCommandResult.Rendered();
            }

            if (result.GoToResult is not null)
            {
                searchResults.GoToResult(target.State, target.Side, result.GoToResult);
                return ApplicationCommandResult.Rendered();
            }

            if (result.DiscardResults)
                return ApplicationCommandResult.Rendered();

            if (result.Results.Count == 0)
            {
                string message = result.Cancelled ? "Search cancelled. No files found." : "No files found.";
                dialogs.Message("Search", message);
                return ApplicationCommandResult.Rendered();
            }

            searchResults.OpenPanel(target.State, request, result.Results, result.Cancelled);
            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            resetFunctionKeyLayer();
        }
    }
}
