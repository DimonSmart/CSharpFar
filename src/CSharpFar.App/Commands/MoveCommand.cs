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
        if (sources.Count == 0)
            return ApplicationCommandResult.Rendered();

        string preFill = sources.Count == 1
            ? Path.GetFileName(sources[0]) ?? sources[0]
            : targetState.CurrentDirectory;

        var dialogResult = new FileOperationDialog(context.Screen).ShowMove(
            sources,
            preFill,
            context.BuildFileOperationOptions());
        if (dialogResult is null)
            return ApplicationCommandResult.Rendered();

        var saved = FileOperationCommandHelpers.CaptureScreen(context);

        try
        {
            context.ExecuteFileOperation(new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
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
            new MessageDialog(context.Screen, context.Palette).Show("Move Error", ex.Message);
        }
        finally
        {
            context.Screen.Restore(saved);
        }

        context.RefreshPanels();
        return ApplicationCommandResult.Rendered();
    }
}
