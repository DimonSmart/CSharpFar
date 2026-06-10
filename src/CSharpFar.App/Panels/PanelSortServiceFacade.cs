using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelSortServiceFacade
{
    private readonly PanelController _controller;
    private readonly PanelSortService _sortService = new();
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
        if (state.SearchRequest is null)
        {
            _controller.SetSortMode(state, mode, visibleRows, _panelOptions());
            return;
        }

        string? cursorPath = _controller.CurrentItem(state)?.FullPath;
        if (state.SortMode == mode)
            state.SortDescending = !state.SortDescending;
        else
        {
            state.SortMode = mode;
            state.SortDescending = false;
        }

        SortVirtualPanel(state, cursorPath, visibleRows);
        _controller.NormalizeCursor(state, visibleRows);
    }

    public void SortVirtualPanel(FilePanelState state, string? keepCursorPath, int visibleRows)
    {
        _closeQuickSearchForState(state);
        var sortOptions = new PanelSortOptions
        {
            SortFoldersByExtension = _panelOptions().SortFoldersByExtension,
            KeepParentDirectoryFirst = false,
            DirectoriesFirst = true,
        };
        var sorted = _sortService.Sort(state.Items, state.SortMode, state.SortDescending, sortOptions);
        state.Items.Clear();
        state.Items.AddRange(sorted);

        if (keepCursorPath is null)
        {
            state.CursorIndex = 0;
            state.ScrollOffset = 0;
            _controller.NormalizeCursor(state, visibleRows);
            return;
        }

        int index = state.Items.FindIndex(i => string.Equals(i.FullPath, keepCursorPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            state.CursorIndex = index;

        _controller.NormalizeCursor(state, visibleRows);
    }
}
