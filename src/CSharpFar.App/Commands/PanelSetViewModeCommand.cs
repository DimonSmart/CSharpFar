using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Controllers;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class PanelSetViewModeCommand(PanelSessionState panels, AppSettingsAlias settings, PanelController controller,
    PanelWorkspaceController workspace, Action? saveSettings) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.PanelSetViewMode;

    public bool CanExecute(object? args = null) =>
        args is SetPanelViewModeArgs;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (args is not SetPanelViewModeArgs viewArgs)
            return ApplicationCommandResult.Failure("Missing panel view mode arguments.");

        if (viewArgs.PanelSide == PanelSide.Left)
        {
            panels.LeftViewMode = viewArgs.ViewMode;
            settings.Panels.LeftViewMode = viewArgs.ViewMode.ToString();
        }
        else
        {
            panels.RightViewMode = viewArgs.ViewMode;
            settings.Panels.RightViewMode = viewArgs.ViewMode.ToString();
        }

        controller.MoveCursor(
            workspace.GetPanelState(viewArgs.PanelSide),
            0,
            workspace.VisibleRows(viewArgs.PanelSide));
        saveSettings?.Invoke();
        return ApplicationCommandResult.Rendered();
    }
}
