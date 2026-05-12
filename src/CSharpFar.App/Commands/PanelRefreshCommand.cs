using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed class PanelRefreshCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelRefresh;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is PanelCommandArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not PanelCommandArgs panelArgs)
            return ApplicationCommandResult.Failure("Missing panel arguments.");

        context.SafeRefresh(
            context.GetPanelState(panelArgs.PanelSide),
            context.VisibleRows(panelArgs.PanelSide));
        return ApplicationCommandResult.Rendered();
    }
}
