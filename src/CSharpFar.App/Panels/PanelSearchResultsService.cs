using CSharpFar.App.Dialogs;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelSearchResultsService
{
    public PanelNavigationService? Navigation { get; set; }
    private readonly DialogService _dialogs;
    private readonly PanelController _controller;
    private readonly IHistoryStore _history;
    private readonly Func<AppSettingsAlias.PanelOptionsSettings> _panelOptions;
    private readonly Func<FilePanelState, PanelSide> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Action<FilePanelState> _closeQuickSearchForState;
    private readonly Action<PanelSide> _closeQuickSearchForPanel;
    private readonly Action<FilePanelState, PanelSide> _startWatching;

    public PanelSearchResultsService(
        DialogService dialogs,
        PanelController controller,
        IHistoryStore history,
        Func<AppSettingsAlias.PanelOptionsSettings> panelOptions,
        Func<FilePanelState, PanelSide> panelSideForState,
        Func<PanelSide, int> visibleRows,
        Action<FilePanelState> closeQuickSearchForState,
        Action<PanelSide> closeQuickSearchForPanel,
        Action<FilePanelState, PanelSide> startWatching)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _controller = controller;
        _history = history;
        _panelOptions = panelOptions;
        _panelSideForState = panelSideForState;
        _visibleRows = visibleRows;
        _closeQuickSearchForState = closeQuickSearchForState;
        _closeQuickSearchForPanel = closeQuickSearchForPanel;
        _startWatching = startWatching;
    }

    public void OpenPanel(
        FilePanelState state,
        SearchRequest request,
        IReadOnlyList<SearchResultItem> results,
        bool cancelled)
    {
        var content = new PanelContent(
            PanelLocation.SearchResult(request.RootPath),
            results.Select(ToFilePanelItem),
            PanelProviderCapabilities.SearchResults)
        {
            Title = PanelSearchResultsSummaryBuilder.BuildTitle(request, cancelled),
            ShowCurrentItemFullPath = true,
        };

        Navigation?.Loading?.Cancel(state);
        _closeQuickSearchForState(state);
        _controller.ReplaceContent(
            state,
            content,
            _visibleRows(_panelSideForState(state)),
            options: _panelOptions());
        state.SearchRequest = request;
        state.SearchWasCancelled = cancelled;
    }

    public void ClosePanel(FilePanelState state, PanelSide side)
    {
        _closeQuickSearchForPanel(side);
        var rootPath = state.SearchRequest!.RootPath;
        Navigate(state, PanelLocation.Local(rootPath), loaded =>
        {
            if (loaded) _startWatching(state, _panelSideForState(state));
        });
    }

    public void GoToResult(FilePanelState state, PanelSide side, SearchResultItem result)
    {
        GoToResult(
            state,
            side,
            result.Location,
            result.Kind == SearchResultItemKind.Directory);
    }

    public void GoToResult(FilePanelState state, PanelSide side, FilePanelItem result)
    {
        GoToResult(
            state,
            side,
            result.Location,
            result.IsDirectory);
    }

    private void GoToResult(
        FilePanelState state,
        PanelSide side,
        PanelLocation location,
        bool isDirectory)
    {
        _closeQuickSearchForPanel(side);
        PanelLocation? directory = isDirectory ? location : _controller.GetParentLocation(location);

        if (directory is not { } directoryLocation)
        {
            _dialogs.Message("Search", $"Cannot open search result: {location.SourcePath}");
            return;
        }

        Navigate(state, directoryLocation, loaded =>
        {
            if (!loaded) return;
            PanelSide currentSide = _panelSideForState(state);
            if (!isDirectory)
                _controller.SetCursorByLocation(state, location, _visibleRows(currentSide));
            if (state.SourceId == PanelSourceId.Local)
                _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
            _startWatching(state, currentSide);
        });
    }

    private void Navigate(FilePanelState state, PanelLocation location, Action<bool> completed)
    {
        if (Navigation is { } navigation)
            navigation.Navigate(state, location, preserveOnError: false, completed);
        else
            completed(_controller.TryLoadLocation(state, location, _panelOptions()));
    }

    private static FilePanelItem ToFilePanelItem(SearchResultItem item) =>
        new()
        {
            Name = item.Name,
            FullPath = item.FullPath,
            SourceId = item.SourceId,
            IsDirectory = item.Kind == SearchResultItemKind.Directory,
            Size = item.Size,
            LastWriteTime = item.LastWriteTime,
            Attributes = item.Attributes,
            IsParentDirectory = false,
        };
}
