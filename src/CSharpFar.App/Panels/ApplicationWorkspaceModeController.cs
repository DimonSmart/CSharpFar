using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Panels;

internal sealed class ApplicationWorkspaceModeController
{
    private readonly ScreenRenderer _screen;
    private readonly ApplicationSession _session;
    private readonly PanelQuickSearchController _panelQuickSearch;
    private readonly CommandCompletionController _commandCompletionController;
    private readonly CommandHistoryNavigator _commandHistoryNavigator;
    private readonly Action _closeTopMenu;
    private readonly TerminalSurfaceController _terminalSurface;
    private readonly UiCompositionHost _composition;

    public ApplicationWorkspaceModeController(
        ScreenRenderer screen,
        ApplicationSession session,
        PanelQuickSearchController panelQuickSearch,
        CommandCompletionController commandCompletionController,
        CommandHistoryNavigator commandHistoryNavigator,
        Action closeTopMenu,
        TerminalSurfaceController terminalSurface,
        UiCompositionHost composition)
    {
        _screen = screen;
        _session = session;
        _panelQuickSearch = panelQuickSearch;
        _commandCompletionController = commandCompletionController;
        _commandHistoryNavigator = commandHistoryNavigator;
        _closeTopMenu = closeTopMenu;
        _terminalSurface = terminalSurface;
        _composition = composition;
    }

    public bool TogglePanels()
    {
        ResetTransientNavigationUi();

        if (_session.App.WorkspaceMode == ApplicationWorkspaceMode.HiddenCommandLine)
        {
            _session.App.WorkspaceMode = ApplicationWorkspaceMode.Panels;
            _terminalSurface.ScrollToBottomAndSyncViewport();
            _terminalSurface.ApplyMode();
            return true;
        }

        _session.App.WorkspaceMode = ApplicationWorkspaceMode.HiddenCommandLine;
        _terminalSurface.EnterHiddenMainScreenAtBottom();
        _screen.SetCursorVisible(true);
        _composition.Render();
        return false;
    }

    private void ResetTransientNavigationUi()
    {
        _panelQuickSearch.Close();
        _commandCompletionController.Hide(temporarily: false);
        _closeTopMenu();
        _commandHistoryNavigator.Reset();
        _session.Ui.PanelScrollbarDrag = null;
    }
}
