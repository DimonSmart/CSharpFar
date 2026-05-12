using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class CopyCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Copy;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.CopyFrom) &&
        context.HasCapability(context.PassiveState, PanelProviderCapabilities.CopyTo);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!context.HasCapability(context.ActiveState, PanelProviderCapabilities.CopyFrom))
        {
            context.ShowReadOnlyPanelMessage("Copy");
            return ApplicationCommandResult.Rendered();
        }

        var targetState = context.PassiveState;
        if (!context.HasCapability(targetState, PanelProviderCapabilities.CopyTo))
        {
            new MessageDialog(context.Screen, context.Palette).Show(
                "Copy",
                "Cannot copy to search results panel.\nSearch results are read-only.");
            return ApplicationCommandResult.Rendered();
        }

        var sources = FileOperationCommandHelpers.GetOperationSources(context);
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        var dialogResult = new FileOperationDialog(context.Screen).ShowCopy(
            sources,
            targetState.CurrentDirectory,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        var saved = FileOperationCommandHelpers.CaptureScreen(context);

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = sources,
                Destination = dialogResult.Destination,
                Options = dialogResult.Options,
            });

            context.ActiveState.SelectedPaths.Clear();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.Screen.Restore(saved);
            new MessageDialog(context.Screen, context.Palette).Show("Copy Error", ex.Message);
        }
        finally
        {
            context.Screen.Restore(saved);
        }

        context.RefreshPanelsAfterFileOperation();
        return ApplicationCommandResult.Rendered();
    }
}
