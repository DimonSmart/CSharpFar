using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Panels;

internal sealed class PanelNavigationService
{
    private readonly PanelController _controller;
    private readonly FilePanelSourceRegistry? _sources;
    private readonly Dictionary<PanelSide, Stack<ReturnPoint>> _returnPoints = new();
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
        Action<FilePanelState, PanelSide> startWatching,
        FilePanelSourceRegistry? sources = null)
    {
        _controller = controller;
        _sources = sources;
        _history = history;
        _panelOptions = panelOptions;
        _visibleRows = visibleRows;
        _closeQuickSearchForPanel = closeQuickSearchForPanel;
        _startWatching = startWatching;
    }

    internal sealed record ReturnPoint(
        PanelLocation Location, string ItemName, int CursorIndex, int ScrollOffset,
        string? DisplayTitle, bool ShowFullPath,
        IReadOnlyList<string> SelectedPaths, IReadOnlyList<PanelLocation> SelectedLocations)
    {
        internal IDisposable? Lease { get; init; }
    }

    public ReturnPoint CaptureReturnPoint(FilePanelState state, FilePanelItem item) =>
        new(state.CurrentLocation, item.Name, state.CursorIndex, state.ScrollOffset,
            state.DisplayTitle, state.ShowCurrentItemFullPath,
            state.SelectedPaths.ToArray(), state.SelectedLocations.ToArray());

    public void PushReturnPoint(PanelSide side, ReturnPoint point)
    {
        var lease = _sources?.AcquireUsage(point.Location.SourceId);
        if (!_returnPoints.TryGetValue(side, out var stack))
            _returnPoints[side] = stack = new Stack<ReturnPoint>();
        stack.Push(point with { Lease = lease });
    }

    public void ClearNavigation(PanelSide side)
    {
        if (!_returnPoints.Remove(side, out var stack))
            return;
        while (stack.TryPop(out var frame))
            frame.Lease?.Dispose();
    }

    private bool TryReturn(FilePanelState state, PanelSide side)
    {
        if (!_returnPoints.TryGetValue(side, out var stack) || !stack.TryPeek(out var frame))
            return false;
        if (!_controller.TryLoadLocation(state, frame.Location, _panelOptions(), preserveOnError: true))
            return false;

        stack.Pop();
        frame.Lease?.Dispose();
        if (stack.Count == 0)
            _returnPoints.Remove(side);
        _controller.SetSourceDisplay(state, frame.DisplayTitle, frame.ShowFullPath);
        foreach (string path in frame.SelectedPaths)
            state.SelectedPaths.Add(path);
        foreach (PanelLocation location in frame.SelectedLocations)
            state.SelectedLocations.Add(location);
        _controller.SetCursorByName(state, frame.ItemName, _visibleRows(side));
        if (!state.Items.Any(item => string.Equals(item.Name, frame.ItemName, StringComparison.Ordinal)))
            _controller.SetCursorTo(state, frame.CursorIndex, _visibleRows(side));
        state.ScrollOffset = frame.ScrollOffset;
        _controller.NormalizeCursor(state, _visibleRows(side));
        _startWatching(state, side);
        return true;
    }

    private bool CanReturnFromRoot(FilePanelState state, PanelSide side)
    {
        if (!_returnPoints.TryGetValue(side, out var stack) || stack.Count == 0)
            return false;
        return _sources?.TryGetSource(state.SourceId, out var source) == true &&
            source.IsRootPath(state.SourcePath);
    }

    public void OpenDirectoryItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Enumerate))
            return;

        _closeQuickSearchForPanel(side);
        if (item.IsParentDirectory && CanReturnFromRoot(state, side))
        {
            TryReturn(state, side);
            return;
        }
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
        if (CanReturnFromRoot(activeState, activeSide))
        {
            TryReturn(activeState, activeSide);
            return;
        }
        if (!_controller.TryGoToParent(activeState, _visibleRows(activeSide), _panelOptions()))
            return;

        if (activeState.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = activeState.CurrentDirectory });
        _startWatching(activeState, activeSide);
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
