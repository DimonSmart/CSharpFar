using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelSortServiceFacade
{
    public Action<FilePanelState, int>? Refresh { get; set; }
    private readonly PanelController _controller;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Action<FilePanelState> _closeQuickSearchForState;

    public PanelSortServiceFacade(
        PanelController controller,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Action<FilePanelState> closeQuickSearchForState)
    {
        _controller = controller;
        _panelOptions = panelOptions;
        _closeQuickSearchForState = closeQuickSearchForState;
    }

    public void SetPanelSortMode(FilePanelState state, SortMode mode, int visibleRows)
    {
        _closeQuickSearchForState(state);
        if (state.ContentKind == PanelContentKind.Source)
        {
            if (Refresh is { } refresh)
            {
                if (state.SortMode == mode)
                    state.SortDescending = !state.SortDescending;
                else
                {
                    state.SortMode = mode;
                    state.SortDescending = false;
                }
                refresh(state, visibleRows);
            }
            else
                _controller.SetSortMode(state, mode, visibleRows, _panelOptions());
            return;
        }

        if (state.SortMode == mode)
            state.SortDescending = !state.SortDescending;
        else
        {
            state.SortMode = mode;
            state.SortDescending = false;
        }

        SortVirtualPanel(state, visibleRows);
    }

    public void SortVirtualPanel(FilePanelState state, int visibleRows)
    {
        _closeQuickSearchForState(state);
        _controller.SortVirtualContent(state, visibleRows, _panelOptions());
    }
}
