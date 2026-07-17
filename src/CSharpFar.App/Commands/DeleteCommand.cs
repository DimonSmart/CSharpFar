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

        var sources = FileOperationCommandHelpers.GetOperationSources(context, target.State, target.Committed);
        var sourceLocations = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.Committed);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        string itemName = sources.Count == 1
            ? Path.GetFileName(sources[0]) ?? sources[0]
            : $"{sources.Count} items";

        bool useRecycleBin = target.State.SourceId == PanelSourceId.Local &&
                             context.Settings.FileOperations.UseRecycleBinForDelete &&
                             context.FileOperations.SupportsRecycleBin;
        string confirmation = useRecycleBin
            ? "Do you wish to move to the Recycle Bin?"
            : "Do you wish to delete permanently?";
        if (context.Settings.Ui.ConfirmDelete &&
            !new ConfirmDialog(context.ModalDialogs).Show("Delete", confirmation, itemName))
        {
            return ApplicationCommandResult.Rendered();
        }

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = sources,
                SourceLocations = sourceLocations,
                Options = context.BuildFileOperationOptions() with
                {
                    UseRecycleBinForDelete = useRecycleBin,
                },
            });
            target.State.SelectedPaths.Clear();
            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            new MessageDialog(context.ModalDialogs).Show("Delete Error", ex.Message);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }
}
