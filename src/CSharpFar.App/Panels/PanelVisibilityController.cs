using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Panels;

internal sealed class PanelVisibilityController
{
    private readonly ScreenRenderer _screen;
    private readonly ApplicationSession _session;
    private readonly PanelWorkspaceController _workspace;
    private readonly PanelQuickSearchController _panelQuickSearch;
    private readonly CommandCompletionController _commandCompletionController;
    private readonly CommandHistoryNavigator _commandHistoryNavigator;
    private readonly TerminalSurfaceController _terminalSurface;
    private readonly ApplicationRenderCoordinator _renderCoordinator;

    public PanelVisibilityController(
        ScreenRenderer screen,
        ApplicationSession session,
        PanelWorkspaceController workspace,
        PanelQuickSearchController panelQuickSearch,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        TerminalSurfaceController terminalSurface,
        ApplicationRenderCoordinator renderCoordinator)
    {
        _screen = screen;
        _session = session;
        _workspace = workspace;
        _panelQuickSearch = panelQuickSearch;
        _commandCompletionController = commandCompletionController;
        _commandHistoryNavigator = commandHistoryNavigator;
        _terminalSurface = terminalSurface;
        _renderCoordinator = renderCoordinator;
    }

    public bool TogglePanels()
    {
        ResetTransientNavigationUi();

        if (_session.App.HiddenPanels == HiddenPanels.Both)
        {
            _session.App.HiddenPanels = HiddenPanels.None;
            _terminalSurface.ScrollToBottomAndSyncViewport();
            _terminalSurface.ApplyMode();
            return true;
        }

        _session.App.HiddenPanels = HiddenPanels.Both;
        _terminalSurface.EnterHiddenMainScreenAtBottom();
        _screen.SetCursorVisible(true);
        _renderCoordinator.RenderCommandLineOnlyUntilStable();
        return false;
    }

    public bool TogglePanel(PanelSide side)
    {
        ResetTransientNavigationUi();

        var flag = PanelWorkspaceController.HiddenPanelFlag(side);
        bool wasHidden = (_session.App.HiddenPanels & flag) != 0;

        if (wasHidden)
        {
            _session.App.HiddenPanels &= ~flag;
            _terminalSurface.ScrollToBottomAndSyncViewport();
        }
        else
        {
            _session.App.HiddenPanels |= flag;
        }

        _workspace.EnsureActivePanelVisible();

        if (_session.App.HiddenPanels == HiddenPanels.Both)
        {
            _terminalSurface.EnterHiddenMainScreenAtBottom();
            _screen.SetCursorVisible(true);
            _renderCoordinator.RenderCommandLineOnlyUntilStable();
            return false;
        }

        _terminalSurface.ApplyMode();
        return true;
    }

    private void ResetTransientNavigationUi()
    {
        _panelQuickSearch.Close();
        _commandCompletionController.Hide(temporarily: false);
        _commandHistoryNavigator.Reset();
        _session.Ui.PanelScrollbarDrag = null;
    }
}
