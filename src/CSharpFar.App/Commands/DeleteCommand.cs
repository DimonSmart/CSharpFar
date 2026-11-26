using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.App.Viewer;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class DeleteCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, FilePanelSourceRegistry sourceRegistry, PanelFileOperationWorkflow operations, AppSettingsAlias settings) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Delete;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).State, PanelProviderCapabilities.Delete);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Delete");
            return ApplicationCommandResult.Rendered();
        }

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(controller, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        string itemName = sources.Count == 1
            ? sourceRegistry.TryGetSource(sources[0].SourceId, out var source) ? source.GetFileName(sources[0].SourcePath) : sources[0].SourcePath
            : $"{sources.Count} items";

        bool useRecycleBin = sources.Count > 0 &&
                             sources.All(location => location.SourceId == PanelSourceId.Local) &&
                             settings.FileOperations.UseRecycleBinForDelete &&
                             operations.SupportsRecycleBin;
        string confirmation = useRecycleBin
            ? "Do you wish to move to the Recycle Bin?"
            : "Do you wish to delete permanently?";
        if (settings.Ui.ConfirmDelete &&
            !dialogs.Confirm("Delete", confirmation, itemName))
        {
            return ApplicationCommandResult.Rendered();
        }

        bool searchResults = target.State.SearchRequest is not null;
        IReadOnlyList<PanelLocation> operationSources = searchResults
            ? SearchResultsDeleteSupport.CollapseNestedLocations(sources, sourceRegistry)
            : sources;
        FileOperationResult? result = null;

        try
        {
            result = operations.Execute(new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = operationSources,
                Options = operations.BuildOptions() with
                {
                    UseRecycleBinForDelete = useRecycleBin,
                },
            });
            target.State.SelectedLocations.Clear();

            if (searchResults)
            {
                SearchResultsDeleteSupport.ReconcileLocations(target.State, operationSources, result, sourceRegistry);
                controller.NormalizeCursor(target.State, target.VisibleRows);
                controller.RefreshSelectionSummary(target.State);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            dialogs.Message("Delete Error", ex.Message);
        }

        if (searchResults)
            operations.RefreshAfterOperation();
        else
            operations.RefreshPanels();

        return ApplicationCommandResult.Rendered();
    }
}
