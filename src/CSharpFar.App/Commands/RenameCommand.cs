using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class RenameCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Rename;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.Rename);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!context.HasCapability(target.State, PanelProviderCapabilities.Rename))
        {
            context.ShowReadOnlyPanelMessage("Rename");
            return ApplicationCommandResult.Rendered();
        }

        FilePanelItem? item = target.Committed is { } committed
            ? ApplicationCommandContext.TryResolveCommittedCurrentItem(target.State, committed, out var committedItem)
                ? committedItem
                : null
            : context.Controller.CurrentItem(target.State);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        string initialName = item.Name;
        var dialogResult = new FileOperationDialog(context.ModalDialogs).ShowRename(
            item.FullPath,
            initialName,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        if (string.Equals(dialogResult.Destination, initialName, StringComparison.Ordinal))
            return ApplicationCommandResult.Rendered();

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [item.FullPath],
                SourceLocations = [item.Location],
                Destination = dialogResult.Destination,
                DestinationLocation = BuildDestinationLocation(context, target.State, dialogResult.Destination),
                Options = dialogResult.Options,
            });

            target.State.SelectedPaths.Clear();
            target.State.SelectedLocations.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            new MessageDialog(context.ModalDialogs).Show("Rename Error", ex.Message);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }

    private static PanelLocation BuildDestinationLocation(
        ApplicationCommandContext context,
        FilePanelState state,
        string destination)
    {
        if (state.SourceId == PanelSourceId.Local)
            return PanelLocation.Local(destination);

        string path = destination.Contains('/')
            ? destination
            : context.CombinePanelPath(state, destination);
        return new PanelLocation(state.SourceId, path);
    }
}
