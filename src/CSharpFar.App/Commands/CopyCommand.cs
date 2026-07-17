using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CopyCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Copy;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.CopyFrom) &&
        context.HasCapability(context.ResolvePanelTarget(args).PassiveState, PanelProviderCapabilities.CopyTo);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!context.HasCapability(target.State, PanelProviderCapabilities.CopyFrom))
        {
            context.ShowReadOnlyPanelMessage("Copy");
            return ApplicationCommandResult.Rendered();
        }

        if (!context.HasCapability(target.PassiveState, PanelProviderCapabilities.CopyTo))
        {
            new MessageDialog(context.ModalDialogs).Show(
                "Copy",
                "Cannot copy to search results panel.\nSearch results are read-only.");
            return ApplicationCommandResult.Rendered();
        }

        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !ApplicationCommandContext.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
            return ApplicationCommandResult.Rendered();

        var sources = FileOperationCommandHelpers.GetOperationSources(context, target.State, target.ActiveCommitted);
        var sourceLocations = FileOperationCommandHelpers.GetOperationSourceLocations(context, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var dialogResult = new FileOperationDialog(context.ModalDialogs).ShowCopy(
            sources,
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = sources,
                SourceLocations = sourceLocations,
                Destination = dialogResult.Destination,
                DestinationLocation = target.PassiveState.SourceId == PanelSourceId.Local
                    ? PanelLocation.Local(dialogResult.Destination)
                    : new PanelLocation(target.PassiveState.SourceId, dialogResult.Destination),
                Options = dialogResult.Options,
            });

            target.State.SelectedPaths.Clear();
            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            new MessageDialog(context.ModalDialogs).Show("Copy Error", ex.Message);
        }

        context.RefreshPanelsAfterFileOperation();
        return ApplicationCommandResult.Rendered();
    }
}
