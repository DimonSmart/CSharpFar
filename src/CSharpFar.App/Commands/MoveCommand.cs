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

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var dialogResult = new FileOperationDialog(context.Dialogs, context.Fields).ShowMove(
            sources.Select(location => location.SourcePath).ToArray(),
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory,
            context.BuildFileOperationOptions(),
            result => DestinationTemplatePreview.Show(context, new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                UseDestinationTemplate = result.UseDestinationTemplate,
                Destination = BuildDestinationLocation(context, target.PassiveState, result.Destination, sources.Count),
                Options = result.Options,
            }));
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                UseDestinationTemplate = dialogResult.UseDestinationTemplate,
                Destination = BuildDestinationLocation(context, target.PassiveState, dialogResult.Destination, sources.Count),
                Options = dialogResult.Options,
            });

            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Dialogs.Message("Move Error", ex.Message);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private static PanelLocation BuildDestinationLocation(
        ApplicationCommandContext context,
        FilePanelState destinationState,
        string destination,
        int sourceCount)
    {
        if (destinationState.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = sourceCount == 1 && context.TryGetSource(destinationState.SourceId, out var source) && !source.PathSeparators.Any(destination.Contains)
            ? context.CombinePanelPath(destinationState, destination)
            : destination;
        return new PanelLocation(destinationState.SourceId, path);
    }
}
