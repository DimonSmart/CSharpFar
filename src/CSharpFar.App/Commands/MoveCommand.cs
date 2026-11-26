using CSharpFar.Ui;
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

internal sealed class MoveCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, FormFieldFactory fields, FilePanelSourceRegistry sourceRegistry, PanelFileOperationWorkflow operations) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.RenameOrMove;

    public bool CanExecute(object? args = null) =>
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).State, PanelProviderCapabilities.MoveFrom) &&
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).PassiveState, PanelProviderCapabilities.MoveTo);

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.MoveFrom))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Move");
            return ApplicationCommandResult.Rendered();
        }

        if (!FileOperationCommandHelpers.HasCapability(target.PassiveState, PanelProviderCapabilities.MoveTo))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Move");
            return ApplicationCommandResult.Rendered();
        }

        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted) ||
            !PanelCommandTargetResolver.CommittedLocationMatches(target.PassiveState, target.PassiveCommitted))
            return ApplicationCommandResult.Rendered();

        var sources = FileOperationCommandHelpers.GetOperationSourceLocations(controller, target.State, target.ActiveCommitted);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var dialogResult = new FileOperationDialog(dialogs, fields).ShowMove(
            sources.Select(location => location.SourcePath).ToArray(),
            target.PassiveCommitted?.CurrentDirectory ?? target.PassiveState.CurrentDirectory,
            operations.BuildOptions(),
            result => operations.Preview(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                UseDestinationTemplate = result.UseDestinationTemplate,
                Destination = BuildDestinationLocation(target.PassiveState, result.Destination, sources.Count),
                Options = result.Options,
            }));
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        try
        {
            operations.Execute(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = sources,
                UseDestinationTemplate = dialogResult.UseDestinationTemplate,
                Destination = BuildDestinationLocation(target.PassiveState, dialogResult.Destination, sources.Count),
                Options = dialogResult.Options,
            });

            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            dialogs.Message("Move Error", ex.Message);
        }

        operations.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private PanelLocation BuildDestinationLocation(
        FilePanelState destinationState,
        string destination,
        int sourceCount)
    {
        if (destinationState.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = sourceCount == 1 && sourceRegistry.TryGetSource(destinationState.SourceId, out var source) && !source.PathSeparators.Any(destination.Contains)
            ? FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, destinationState, destination)
            : destination;
        return new PanelLocation(destinationState.SourceId, path);
    }
}
