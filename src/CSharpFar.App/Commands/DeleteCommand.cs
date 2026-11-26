using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class DeleteCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Delete;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.Delete);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Delete");
            return ApplicationCommandResult.Rendered();
        }

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        string itemName = sources.Count == 1
            ? context.TryGetSource(sources[0].SourceId, out var source) ? source.GetFileName(sources[0].SourcePath) : sources[0].SourcePath
            : $"{sources.Count} items";

        bool useRecycleBin = sources.Count > 0 &&
                             sources.All(location => location.SourceId == PanelSourceId.Local) &&
                             context.Settings.FileOperations.UseRecycleBinForDelete &&
                             context.FileOperations.SupportsRecycleBin;
        string confirmation = useRecycleBin
            ? "Do you wish to move to the Recycle Bin?"
            : "Do you wish to delete permanently?";
        if (context.Settings.Ui.ConfirmDelete &&
            !context.Dialogs.Confirm("Delete", confirmation, itemName))
        {
            return ApplicationCommandResult.Rendered();
        }

        bool searchResults = target.State.SearchRequest is not null;
        IReadOnlyList<PanelLocation> operationSources = searchResults
            ? sources.GroupBy(location => location.SourceId).SelectMany(group =>
                group.Key == PanelSourceId.Local
                    ? SearchResultsDeleteSupport.CollapseNestedSources(group.Select(location => location.SourcePath).ToArray()).Select(PanelLocation.Local)
                    : group).ToArray()
            : sources;
        FileOperationResult? result = null;

        try
        {
            result = context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = operationSources,
                Options = context.BuildFileOperationOptions() with
                {
                    UseRecycleBinForDelete = useRecycleBin,
                },
            });
            target.State.SelectedLocations.Clear();

            if (searchResults)
            {
                SearchResultsDeleteSupport.Reconcile(target.State, operationSources.Where(location => location.SourceId == PanelSourceId.Local).Select(location => location.SourcePath).ToArray(), result);
                context.Controller.NormalizeCursor(target.State, target.VisibleRows);
                context.Controller.RefreshSelectionSummary(target.State);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Dialogs.Message("Delete Error", ex.Message);
        }

        if (searchResults)
            context.RefreshPanelsAfterFileOperation();
        else
            context.RefreshPanels();

        return ApplicationCommandResult.Rendered();
    }
}
