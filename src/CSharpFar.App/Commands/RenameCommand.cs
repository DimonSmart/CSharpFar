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

internal sealed class RenameCommand(PanelCommandTargetResolver targetResolver, PanelController controller, DialogService dialogs, FormFieldFactory fields, FilePanelSourceRegistry sourceRegistry, PanelFileOperationWorkflow operations) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Rename;

    public bool CanExecute(object? args = null) =>
        FileOperationCommandHelpers.HasCapability(targetResolver.ResolvePanelTarget(args).State, PanelProviderCapabilities.Rename);

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!FileOperationCommandHelpers.HasCapability(target.State, PanelProviderCapabilities.Rename))
        {
            FileOperationCommandHelpers.ShowReadOnlyPanelMessage(dialogs, "Rename");
            return ApplicationCommandResult.Rendered();
        }

        FilePanelItem? item = PanelCommandTargetResolver.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, controller, out var resolvedItem) ? resolvedItem : null;
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        string initialName = item.Name;
        var dialogResult = new FileOperationDialog(dialogs, fields).ShowRename(
            item.FullPath,
            initialName,
            operations.BuildOptions(),
            result => operations.Preview(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [item.Location],
                UseDestinationTemplate = result.UseDestinationTemplate,
                Destination = BuildDestinationLocation(target.State, result.Destination),
                Options = result.Options,
            }));
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        if (!dialogResult.UseDestinationTemplate && string.Equals(dialogResult.Destination, initialName, StringComparison.Ordinal))
            return ApplicationCommandResult.Rendered();

        try
        {
            operations.Execute(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [item.Location],
                UseDestinationTemplate = dialogResult.UseDestinationTemplate,
                Destination = BuildDestinationLocation(target.State, dialogResult.Destination),
                Options = dialogResult.Options,
            });

            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            dialogs.Message("Rename Error", ex.Message);
        }

        operations.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private PanelLocation BuildDestinationLocation(
        FilePanelState state,
        string destination)
    {
        if (state.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = sourceRegistry.TryGetSource(state.SourceId, out var source) && source.PathSeparators.Any(destination.Contains)
            ? destination
            : FileOperationCommandHelpers.CombinePanelPath(sourceRegistry, state, destination);
        return new PanelLocation(state.SourceId, path);
    }
}
