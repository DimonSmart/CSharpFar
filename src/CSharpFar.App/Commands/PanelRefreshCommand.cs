using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class PanelRefreshCommand(PanelWorkspaceController workspace, PanelRefreshService panelRefresh) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelRefresh;

    public bool CanExecute(object? args = null) =>
        args is PanelCommandArgs;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (args is not PanelCommandArgs panelArgs)
            return ApplicationCommandResult.Failure("Missing panel arguments.");

        panelRefresh.SafeRefresh(
            workspace.GetPanelState(panelArgs.PanelSide),
            workspace.VisibleRows(panelArgs.PanelSide));
        return ApplicationCommandResult.Rendered();
    }
}
