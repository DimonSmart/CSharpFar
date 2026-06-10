using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelRefreshService
{
    private readonly PanelController _controller;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState> _closeQuickSearchForState;
    private readonly Action<FilePanelState, int> _refreshSearchResultsPanel;

    public PanelRefreshService(
        PanelController controller,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState> closeQuickSearchForState,
        Action<FilePanelState, int> refreshSearchResultsPanel)
    {
        _controller = controller;
        _panelOptions = panelOptions;
        _visibleRows = visibleRows;
        _closeQuickSearchForState = closeQuickSearchForState;
        _refreshSearchResultsPanel = refreshSearchResultsPanel;
    }

    public void RefreshPanels(FilePanelState left, FilePanelState right)
    {
        SafeRefresh(left, _visibleRows(PanelSide.Left));
        SafeRefresh(right, _visibleRows(PanelSide.Right));
    }

    public void RefreshPanelsAfterFileOperation(FilePanelState left, FilePanelState right)
    {
        RefreshPanelAfterFileOperation(left, PanelSide.Left);
        RefreshPanelAfterFileOperation(right, PanelSide.Right);
    }

    public void SafeRefresh(FilePanelState state, int visibleRows)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Refresh))
            return;

        _closeQuickSearchForState(state);
        if (state.SearchRequest is not null)
        {
            _refreshSearchResultsPanel(state, visibleRows);
            return;
        }

        _controller.TryRefreshDirectory(state, visibleRows, _panelOptions());
    }

    private void RefreshPanelAfterFileOperation(FilePanelState state, PanelSide side)
    {
        if (state.SearchRequest is not null)
        {
            state.Summary = PanelSearchResultsSummaryBuilder.BuildSummary(state);
            return;
        }

        SafeRefresh(state, _visibleRows(side));
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
