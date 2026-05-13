using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class DeleteCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Delete;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.Delete);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Delete");
            return ApplicationCommandResult.Rendered();
        }

        var sources = FileOperationCommandHelpers.GetOperationSources(context);
        var sourceLocations = FileOperationCommandHelpers.GetOperationSourceLocations(context);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        string itemName = sources.Count == 1
            ? Path.GetFileName(sources[0]) ?? sources[0]
            : $"{sources.Count} items";

        if (context.Settings.Ui.ConfirmDelete &&
            !new ConfirmDialog(context.Screen).Show("Delete", "Do you wish to move to the Recycle Bin?", itemName))
        {
            return ApplicationCommandResult.Rendered();
        }

        var saved = FileOperationCommandHelpers.CaptureScreen(context);

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = sources,
                SourceLocations = sourceLocations,
                Options = context.BuildFileOperationOptions() with
                {
                    UseRecycleBinForDelete = context.ActiveState.SourceId == PanelSourceId.Local &&
                                             context.Settings.FileOperations.UseRecycleBinForDelete,
                },
            });
            context.ActiveState.SelectedPaths.Clear();
            context.ActiveState.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Screen.Restore(saved);
            new MessageDialog(context.Screen, context.Palette).Show("Delete Error", ex.Message);
        }
        finally
        {
            context.Screen.Restore(saved);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }
}
