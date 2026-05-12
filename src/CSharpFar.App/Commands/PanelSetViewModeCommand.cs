using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class PanelSetViewModeCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelSetViewMode;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        args is SetPanelViewModeArgs;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (args is not SetPanelViewModeArgs viewArgs)
            return ApplicationCommandResult.Failure("Missing panel view mode arguments.");

        if (viewArgs.PanelSide == PanelSide.Left)
        {
            context.LeftViewMode = viewArgs.ViewMode;
            context.Settings.Panels.LeftViewMode = viewArgs.ViewMode.ToString();
        }
        else
        {
            context.RightViewMode = viewArgs.ViewMode;
            context.Settings.Panels.RightViewMode = viewArgs.ViewMode.ToString();
        }

        context.Controller.MoveCursor(
            context.GetPanelState(viewArgs.PanelSide),
            0,
            context.VisibleRows(viewArgs.PanelSide));
        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }
}
