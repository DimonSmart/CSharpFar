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
            context.SortVirtualPanel(state, context.Controller.CurrentItem(state)?.FullPath);
            context.Controller.MoveCursor(state, 0, context.VisibleRows(panelArgs.PanelSide));
        }

        return ApplicationCommandResult.Rendered();
    }
}
