using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class RenameCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Rename;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.Rename);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!context.HasCapability(context.ActiveState, PanelProviderCapabilities.Rename))
        {
            context.ShowReadOnlyPanelMessage("Rename");
            return ApplicationCommandResult.Rendered();
        }

        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        string initialName = item.Name;
        var dialogResult = new FileOperationDialog(context.Screen).ShowRename(
            item.FullPath,
            initialName,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        if (string.Equals(dialogResult.Destination, initialName, StringComparison.Ordinal))
            return ApplicationCommandResult.Rendered();

        var saved = FileOperationCommandHelpers.CaptureScreen(context);

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [item.FullPath],
                SourceLocations = [item.Location],
                Destination = dialogResult.Destination,
                DestinationLocation = BuildDestinationLocation(context, dialogResult.Destination),
                Options = dialogResult.Options,
            });

            context.ActiveState.SelectedPaths.Clear();
            context.ActiveState.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Screen.Restore(saved);
            new MessageDialog(context.Screen, context.Palette).Show("Rename Error", ex.Message);
        }
        finally
        {
            context.Screen.Restore(saved);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private static PanelLocation BuildDestinationLocation(
        ApplicationCommandContext context,
        string destination)
    {
        if (context.ActiveState.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = destination.Contains('/')
            ? destination
            : context.CombinePanelPath(context.ActiveState, destination);
        return new PanelLocation(context.ActiveState.SourceId, path);
    }
}
