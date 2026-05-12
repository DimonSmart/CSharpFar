using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class PanelSetSortModeCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelSetSortMode;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is SetPanelSortModeArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not SetPanelSortModeArgs sortArgs)
            return ApplicationCommandResult.Failure("Missing panel sort arguments.");

        context.SetPanelSortMode(
            context.GetPanelState(sortArgs.PanelSide),
            sortArgs.SortMode,
            context.VisibleRows(sortArgs.PanelSide));
        return ApplicationCommandResult.Rendered();
    }
}
