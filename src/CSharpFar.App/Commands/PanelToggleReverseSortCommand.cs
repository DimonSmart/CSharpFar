using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class PanelToggleReverseSortCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelToggleReverseSort;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is PanelCommandArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not PanelCommandArgs panelArgs)
            return ApplicationCommandResult.Failure("Missing panel arguments.");

        var state = context.GetPanelState(panelArgs.PanelSide);
        state.SortDescending = !state.SortDescending;
        if (state.SearchRequest is null)
        {
            context.SafeRefresh(state, context.VisibleRows(panelArgs.PanelSide));
        }
        else
        {
            int visibleRows = context.VisibleRows(panelArgs.PanelSide);
            context.SortVirtualPanel(state, context.Controller.CurrentItem(state)?.FullPath, visibleRows);
            context.Controller.NormalizeCursor(state, visibleRows);
        }

        return ApplicationCommandResult.Rendered();
    }
}
