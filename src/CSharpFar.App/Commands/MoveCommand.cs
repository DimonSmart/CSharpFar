using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class MoveCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.RenameOrMove;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.MoveFrom) &&
        context.HasCapability(context.ResolvePanelTarget(args).PassiveState, PanelProviderCapabilities.MoveTo);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!context.HasCapability(target.State, PanelProviderCapabilities.MoveFrom))
        {
            context.ShowReadOnlyPanelMessage("Move");
            return ApplicationCommandResult.Rendered();
        }

        if (!context.HasCapability(target.PassiveState, PanelProviderCapabilities.MoveTo))
        {
            context.ShowReadOnlyPanelMessage("Move");
            return ApplicationCommandResult.Rendered();
        }

        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
            return ApplicationCommandResult.Rendered();

        var sources = FileOperationCommandHelpers.GetOperationSources(context, target.State, target.ActiveCommitted);
        var sourceLocations = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        if (target.State.SourceId != target.PassiveState.SourceId && sources.Count > 0)
        {
            new MessageDialog(context.ModalDialogs).Show(
                "Move",
                "Cross-provider move is not supported.");
            return ApplicationCommandResult.Rendered();
        }

        var dialogResult = new FileOperationDialog(context.ModalDialogs).ShowMove(
            sources,
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory,
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
                DestinationLocation = BuildDestinationLocation(context, target.State, dialogResult.Destination, sources.Count),
                Options = dialogResult.Options,
            });

            target.State.SelectedPaths.Clear();
            target.State.SelectedLocations.Clear();
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
        FilePanelState sourceState,
        string destination,
        int sourceCount)
    {
        if (sourceState.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = sourceCount == 1 && !destination.Contains('/')
            ? context.CombinePanelPath(sourceState, destination)
            : destination;
        return new PanelLocation(sourceState.SourceId, path);
    }
}
