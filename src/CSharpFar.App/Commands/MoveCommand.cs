using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class MoveCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.RenameOrMove;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.MoveFrom) &&
        context.HasCapability(context.PassiveState, PanelProviderCapabilities.MoveTo);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!context.HasCapability(context.ActiveState, PanelProviderCapabilities.MoveFrom))
        {
            context.ShowReadOnlyPanelMessage("Move");
            return ApplicationCommandResult.Rendered();
        }

        var targetState = context.PassiveState;
        if (!context.HasCapability(targetState, PanelProviderCapabilities.MoveTo))
        {
            context.ShowReadOnlyPanelMessage("Move");
            return ApplicationCommandResult.Rendered();
        }

        var sources = FileOperationCommandHelpers.GetOperationSources(context);
        var sourceLocations = FileOperationCommandHelpers.GetOperationSourceLocations(context);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        if (context.ActiveState.SourceId != context.PassiveState.SourceId && sources.Count > 0)
        {
            new MessageDialog(context.ModalDialogs).Show(
                "Move",
                "Cross-provider move is not supported.");
            return ApplicationCommandResult.Rendered();
        }

        var dialogResult = new FileOperationDialog(context.ModalDialogs).ShowMove(
            sources,
            targetState.CurrentDirectory,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                SourceLocations = sourceLocations,
                Destination = dialogResult.Destination,
                DestinationLocation = BuildDestinationLocation(context, dialogResult.Destination, sources.Count),
                Options = dialogResult.Options,
            });

            context.ActiveState.SelectedPaths.Clear();
            context.ActiveState.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            new MessageDialog(context.ModalDialogs).Show("Move Error", ex.Message);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private static PanelLocation BuildDestinationLocation(
        ApplicationCommandContext context,
        string destination,
        int sourceCount)
    {
        if (context.ActiveState.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = sourceCount == 1 && !destination.Contains('/')
            ? context.CombinePanelPath(context.ActiveState, destination)
            : destination;
        return new PanelLocation(context.ActiveState.SourceId, path);
    }
}
