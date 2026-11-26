using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class PanelToggleReverseSortCommand(PanelWorkspaceController workspace, PanelRefreshService panelRefresh, PanelSortServiceFacade panelSort) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelToggleReverseSort;

    public bool CanExecute(object? args = null) =>
        args is PanelCommandArgs;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (args is not PanelCommandArgs panelArgs)
            return ApplicationCommandResult.Failure("Missing panel arguments.");

        var state = workspace.GetPanelState(panelArgs.PanelSide);
        state.SortDescending = !state.SortDescending;
        if (state.ContentKind == PanelContentKind.Source)
        {
            panelRefresh.SafeRefresh(state, workspace.VisibleRows(panelArgs.PanelSide));
        }
        else
        {
            int visibleRows = workspace.VisibleRows(panelArgs.PanelSide);
            panelSort.SortVirtualPanel(state, visibleRows);
        }

        return ApplicationCommandResult.Rendered();
    }
}
