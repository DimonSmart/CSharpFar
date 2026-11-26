using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class PanelSetSortModeCommand(PanelWorkspaceController workspace, PanelSortServiceFacade panelSort) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelSetSortMode;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is SetPanelSortModeArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not SetPanelSortModeArgs sortArgs)
            return ApplicationCommandResult.Failure("Missing panel sort arguments.");

        panelSort.SetPanelSortMode(
            workspace.GetPanelState(sortArgs.PanelSide),
            sortArgs.SortMode,
            workspace.VisibleRows(sortArgs.PanelSide));
        return ApplicationCommandResult.Rendered();
    }
}
