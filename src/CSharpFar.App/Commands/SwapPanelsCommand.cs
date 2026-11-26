using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.UserMenu;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
namespace CSharpFar.App.Commands;

internal sealed class SwapPanelsCommand(PanelWorkspaceController workspace, PanelSessionState panels, PanelAutoRefreshService autoRefresh, Action resetTransientNavigationUi) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.SwapPanels;

    public bool CanExecute(object? args = null) =>
        workspace.IsPanelsMode;

    public ApplicationCommandResult Execute(object? args = null)
    {
        resetTransientNavigationUi();
        (panels.Left, panels.Right) = (panels.Right, panels.Left);
        (panels.LeftViewMode, panels.RightViewMode) = (panels.RightViewMode, panels.LeftViewMode);
        autoRefresh.StartWatching(panels.Left, PanelSide.Left);
        autoRefresh.StartWatching(panels.Right, PanelSide.Right);
        return ApplicationCommandResult.Rendered();
    }
}
