using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelNavigationService
{
    private readonly PanelController _controller;
    private readonly IHistoryStore _history;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<PanelSide> _closeQuickSearchForPanel;
    private readonly Action<FilePanelState, PanelSide> _startWatching;

    public PanelNavigationService(
        PanelController controller,
        IHistoryStore history,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, int> visibleRows,
        Action<PanelSide> closeQuickSearchForPanel,
        Action<FilePanelState, PanelSide> startWatching)
    {
        _controller = controller;
        _history = history;
        _panelOptions = panelOptions;
        _visibleRows = visibleRows;
        _closeQuickSearchForPanel = closeQuickSearchForPanel;
        _startWatching = startWatching;
    }

    public void OpenDirectoryItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Enumerate))
            return;

        _closeQuickSearchForPanel(side);
        bool loaded = item.IsParentDirectory
            ? _controller.TryGoToParent(state, _visibleRows(side), _panelOptions())
            : _controller.TryLoadLocation(state, item.Location, _panelOptions());

        if (!loaded)
            return;

        if (state.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
        _startWatching(state, side);
    }

    public void TryGoUp(FilePanelState activeState, PanelSide activeSide)
    {
        if (!HasCapability(activeState, PanelProviderCapabilities.Enumerate))
            return;

        _closeQuickSearchForPanel(activeSide);
        if (!_controller.TryGoToParent(activeState, _visibleRows(activeSide), _panelOptions()))
            return;

        if (activeState.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = activeState.CurrentDirectory });
        _startWatching(activeState, activeSide);
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
