using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelSearchResultsService
{
    private readonly ScreenRenderer _screen;
    private readonly ModalDialogHost _modalDialogs;
    private readonly ISearchService _searchService;
    private readonly Func<ConsolePalette> _palette;
    private readonly PanelController _controller;
    private readonly IHistoryStore _history;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<FilePanelState, PanelSide> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState> _closeQuickSearchForState;
    private readonly Action<PanelSide> _closeQuickSearchForPanel;
    private readonly Action<FilePanelState, PanelSide> _startWatching;
    private readonly Action<FilePanelState, string?, int> _sortVirtualPanel;

    public PanelSearchResultsService(
        ScreenRenderer screen,
        ModalDialogHost modalDialogs,
        ISearchService searchService,
        Func<ConsolePalette> palette,
        PanelController controller,
        IHistoryStore history,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<FilePanelState, PanelSide> panelSideForState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState> closeQuickSearchForState,
        Action<PanelSide> closeQuickSearchForPanel,
        Action<FilePanelState, PanelSide> startWatching,
        Action<FilePanelState, string?, int> sortVirtualPanel)
    {
        _screen = screen;
        _modalDialogs = modalDialogs;
        _searchService = searchService;
        _palette = palette;
        _controller = controller;
        _history = history;
        _panelOptions = panelOptions;
        _panelSideForState = panelSideForState;
        _visibleRows = visibleRows;
        _closeQuickSearchForState = closeQuickSearchForState;
        _closeQuickSearchForPanel = closeQuickSearchForPanel;
        _startWatching = startWatching;
        _sortVirtualPanel = sortVirtualPanel;
    }

    public void OpenPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled)
    {
        _closeQuickSearchForState(state);
        state.CurrentLocation = PanelLocation.SearchResult(request.RootPath);
        state.Items.Clear();
        state.Items.AddRange(results.Select(ToFilePanelItem));
        state.SelectedPaths.Clear();
        state.SelectedLocations.Clear();
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
        state.ProviderCapabilities = PanelProviderCapabilities.SearchResults;
        state.DisplayTitle = PanelSearchResultsSummaryBuilder.BuildTitle(request, cancelled);
        state.ShowCurrentItemFullPath = true;
        state.SearchRequest = request;
        state.SearchWasCancelled = cancelled;
        state.AutoRefreshState = null;
        _sortVirtualPanel(state, null, _visibleRows(_panelSideForState(state)));
        state.Summary = PanelSearchResultsSummaryBuilder.BuildSummary(state);
    }

    public void ClosePanel(FilePanelState state, PanelSide side)
    {
        _closeQuickSearchForPanel(side);
        var rootPath = state.SearchRequest!.RootPath;
        state.SearchRequest = null;
        state.SearchWasCancelled = false;
        state.ShowCurrentItemFullPath = false;
        state.DisplayTitle = null;
        if (_controller.TryLoadDirectory(state, rootPath, _panelOptions()))
            _startWatching(state, side);
    }

    public void GoToResult(FilePanelState state, PanelSide side, SearchResultItem result)
    {
        GoToResult(
            state,
            side,
            result.FullPath,
            result.Name,
            result.Kind == SearchResultItemKind.Directory);
    }

    public void GoToResult(FilePanelState state, PanelSide side, FilePanelItem result)
    {
        GoToResult(
            state,
            side,
            result.FullPath,
            result.Name,
            result.IsDirectory);
    }

    public void RefreshPanel(FilePanelState state, int visibleRows)
    {
        if (state.SearchRequest is null)
            return;

        var previousItems = state.Items.ToList();
        var previousSelectedPaths = state.SelectedPaths.ToList();
        int previousCursor = state.CursorIndex;
        int previousScroll = state.ScrollOffset;
        string? cursorPath = _controller.CurrentItem(state)?.FullPath;

        SearchRunResult result;
        try
        {
            result = new SearchProgressDialog(_modalDialogs, _searchService, _palette()).Show(state.SearchRequest);
        }
        catch
        {
            RestorePreviousResults(state, previousItems, previousSelectedPaths, previousCursor, previousScroll, visibleRows);
            return;
        }

        if (result.GoToResult is not null)
        {
            GoToResult(state, _panelSideForState(state), result.GoToResult);
            return;
        }

        if (result.DiscardResults || result.Cancelled)
        {
            RestorePreviousResults(state, previousItems, previousSelectedPaths, previousCursor, previousScroll, visibleRows);
            return;
        }

        state.Items.Clear();
        state.Items.AddRange(result.Results.Select(ToFilePanelItem));
        state.SelectedPaths.Clear();
        state.SearchWasCancelled = false;
        state.DisplayTitle = PanelSearchResultsSummaryBuilder.BuildTitle(state.SearchRequest, cancelled: false);
        _sortVirtualPanel(state, cursorPath, visibleRows);
        state.Summary = PanelSearchResultsSummaryBuilder.BuildSummary(state);
        _controller.NormalizeCursor(state, visibleRows);
    }

    private void GoToResult(
        FilePanelState state,
        PanelSide side,
        string fullPath,
        string name,
        bool isDirectory)
    {
        _closeQuickSearchForPanel(side);
        string? directoryPath = isDirectory
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            new MessageDialog(_modalDialogs).Show("Search", $"Cannot open search result: {fullPath}");
            return;
        }

        try
        {
            if (_controller.TryLoadDirectory(state, directoryPath, _panelOptions()))
            {
                if (!isDirectory)
                    _controller.SetCursorByName(state, name, _visibleRows(side));

                _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
                _startWatching(state, side);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            new MessageDialog(_modalDialogs).Show("Search", ex.Message);
        }
    }

    private void RestorePreviousResults(
        FilePanelState state,
        List<FilePanelItem> previousItems,
        List<string> previousSelectedPaths,
        int previousCursor,
        int previousScroll,
        int visibleRows)
    {
        state.Items.Clear();
        state.Items.AddRange(previousItems);
        state.SelectedPaths.Clear();
        foreach (string selectedPath in previousSelectedPaths)
            state.SelectedPaths.Add(selectedPath);
        state.CursorIndex = previousCursor;
        state.ScrollOffset = previousScroll;
        _controller.NormalizeCursor(state, visibleRows);
        state.Summary = PanelSearchResultsSummaryBuilder.BuildSummary(state);
    }

    private static FilePanelItem ToFilePanelItem(SearchResultItem item) =>
        new()
        {
            Name = item.Name,
            FullPath = item.FullPath,
            IsDirectory = item.Kind == SearchResultItemKind.Directory,
            Size = item.Size,
            LastWriteTime = item.LastWriteTime,
            Attributes = item.Attributes,
            IsParentDirectory = false,
        };
}
