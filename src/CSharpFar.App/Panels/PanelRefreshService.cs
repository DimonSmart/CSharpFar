using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelRefreshService
{
    public Action<FilePanelState>? RefreshRequested { get; set; }
    public PanelLoadingCoordinator? Loading { get; set; }
    public Func<FilePanelState, PanelSide>? ResolveSide { get; set; }
    private readonly PanelController _controller;
    private readonly Action<FilePanelState>? _afterRefresh;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState> _closeQuickSearchForState;

    public PanelRefreshService(
        PanelController controller,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState> closeQuickSearchForState,
        Action<FilePanelState>? afterRefresh = null)
    {
        _controller = controller;
        _afterRefresh = afterRefresh;
        _panelOptions = panelOptions;
        _visibleRows = visibleRows;
        _closeQuickSearchForState = closeQuickSearchForState;
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

    public void SafeRefresh(FilePanelState state, int visibleRows, Action<bool>? completed = null)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Refresh))
            return;

        RefreshRequested?.Invoke(state);
        _closeQuickSearchForState(state);

        if (state.ContentKind == PanelContentKind.Virtual)
            return;

        if (Loading is { } loading)
        {
            loading.Refresh(state, _panelOptions(),
                () => ResolveSide is null ? visibleRows : _visibleRows(ResolveSide(state)),
                loaded => { if (loaded) _afterRefresh?.Invoke(state); completed?.Invoke(loaded); });
        }
        else
        {
            bool loaded = _controller.TryRefreshDirectory(state, visibleRows, _panelOptions());
            if (loaded) _afterRefresh?.Invoke(state);
            completed?.Invoke(loaded);
        }
    }

    private void RefreshPanelAfterFileOperation(FilePanelState state, PanelSide side)
    {
        if (state.ContentKind == PanelContentKind.Virtual)
        {
            return;
        }

        SafeRefresh(state, _visibleRows(side));
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
