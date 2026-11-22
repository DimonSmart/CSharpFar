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

    // Assigned during final application composition, after the UI queue exists.
    public AsyncPluginPanelLoader? AsyncLoads { get; set; }
    public Action<string>? ReportLoadFailure { get; set; }

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
        string? DisplayTitle, string? DisplayTitleBasePath, bool ShowFullPath,
        IReadOnlyList<string> SelectedPaths, IReadOnlyList<PanelLocation> SelectedLocations)
    {
        internal IDisposable? Lease { get; init; }
    }

    public ReturnPoint CaptureReturnPoint(FilePanelState state, FilePanelItem item) =>
        new(state.CurrentLocation, item.Name, state.CursorIndex, state.ScrollOffset,
            state.DisplayTitle, state.DisplayTitleBasePath, state.ShowCurrentItemFullPath,
            state.SelectedPaths.ToArray(), state.SelectedLocations.ToArray());

    public void PushReturnPoint(PanelSide side, ReturnPoint point)
    {
        var lease = _sources?.AcquireUsage(point.Location.SourceId);
        if (!_returnPoints.TryGetValue(side, out var stack))
            _returnPoints[side] = stack = new Stack<ReturnPoint>();
        stack.Push(point with { Lease = lease });
    }

    /// <summary>
    /// The inner source may have no natural parent at its root. Insert the generic
    /// parent entry there so Enter and ordinary parent hotkeys share the same route.
    /// This is panel UI metadata, not a path inside the container provider.
    /// </summary>
    public void EnsureNestedRootParent(FilePanelState state, PanelSide side)
    {
        if (!CanReturnFromRoot(state, side) || state.Items.Any(item => item.IsParentDirectory))
            return;

        state.Items.Insert(0, new FilePanelItem
        {
            Name = "..",
            FullPath = state.SourcePath,
            SourceId = state.SourceId,
            IsDirectory = true,
            IsParentDirectory = true,
            Attributes = FileAttributes.Directory,
        });
        state.CursorIndex = Math.Min(state.CursorIndex + 1, state.Items.Count - 1);
        if (state.ScrollOffset > 0)
            state.ScrollOffset++;
    }

    public void EnterNestedSource(FilePanelState state, PanelSide side, ReturnPoint point)
    {
        PushReturnPoint(side, point);
        EnsureNestedRootParent(state, side);
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

        if (AsyncLoads is not null && AsyncPluginPanelLoader.CanLoad(frame.Location))
        {
            AsyncLoads.Load(state, frame.Location, _panelOptions(),
                onSuccess: () =>
                {
                    if (_returnPoints.TryGetValue(side, out var currentStack) &&
                        currentStack.TryPeek(out var current) && ReferenceEquals(frame, current))
                        CompleteReturn(state, side, frame, currentStack);
                },
                onFailure: error => ReportLoadFailure?.Invoke(error.Message));
            return true;
        }

        AsyncLoads?.CancelPending(state);
        if (!_controller.TryLoadLocation(state, frame.Location, _panelOptions(), preserveOnError: true))
            return false;
        CompleteReturn(state, side, frame, stack);
        return true;
    }

    private void CompleteReturn(FilePanelState state, PanelSide side, ReturnPoint frame, Stack<ReturnPoint> stack)
    {
        // Keep the source visible before releasing the navigation frame's lease.
        _sources?.RetainSource(frame.Location.SourceId);
        stack.Pop();
        frame.Lease?.Dispose();
        if (stack.Count == 0)
            _returnPoints.Remove(side);
        _controller.SetSourceDisplay(state, frame.DisplayTitle, frame.ShowFullPath, frame.DisplayTitleBasePath);
        foreach (string path in frame.SelectedPaths)
            state.SelectedPaths.Add(path);
        foreach (PanelLocation location in frame.SelectedLocations)
            state.SelectedLocations.Add(location);
        _controller.RefreshSelectionSummary(state);
        _controller.SetCursorByName(state, frame.ItemName, _visibleRows(side));
        if (!state.Items.Any(item => string.Equals(item.Name, frame.ItemName, StringComparison.Ordinal)))
            _controller.SetCursorTo(state, frame.CursorIndex, _visibleRows(side));
        state.ScrollOffset = frame.ScrollOffset;
        _controller.NormalizeCursor(state, _visibleRows(side));
        EnsureNestedRootParent(state, side);
        _startWatching(state, side);
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

        if (AsyncLoads is not null && AsyncPluginPanelLoader.CanLoad(item.Location))
        {
            if (item.IsParentDirectory && item.SourcePath == state.SourcePath)
                return;
            string childName = item.IsParentDirectory
                ? state.SourcePath.TrimEnd('/').Split('/').LastOrDefault() ?? state.SourcePath
                : string.Empty;
            AsyncLoads.Load(state, item.Location, _panelOptions(),
                onSuccess: () =>
                {
                    if (item.IsParentDirectory)
                        _controller.SetCursorByName(state, childName, _visibleRows(side));
                    FinishNavigation(state, side);
                },
                onFailure: error => ReportLoadFailure?.Invoke(error.Message));
            return;
        }

        AsyncLoads?.CancelPending(state);
        bool loaded = item.IsParentDirectory
            ? _controller.TryGoToParent(state, _visibleRows(side), _panelOptions())
            : _controller.TryLoadLocation(state, item.Location, _panelOptions());
        if (loaded)
            FinishNavigation(state, side);
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

        if (AsyncLoads is not null && AsyncPluginPanelLoader.CanLoad(activeState.CurrentLocation))
        {
            var parent = activeState.Items.FirstOrDefault(item => item.IsParentDirectory);
            if (parent is null || parent.SourcePath == activeState.SourcePath)
                return;
            string childName = activeState.SourcePath.TrimEnd('/').Split('/').LastOrDefault()
                ?? activeState.SourcePath;
            AsyncLoads.Load(activeState, parent.Location, _panelOptions(),
                onSuccess: () =>
                {
                    _controller.SetCursorByName(activeState, childName, _visibleRows(activeSide));
                    FinishNavigation(activeState, activeSide);
                },
                onFailure: error => ReportLoadFailure?.Invoke(error.Message));
            return;
        }

        AsyncLoads?.CancelPending(activeState);
        if (_controller.TryGoToParent(activeState, _visibleRows(activeSide), _panelOptions()))
            FinishNavigation(activeState, activeSide);
    }

    private void FinishNavigation(FilePanelState state, PanelSide side)
    {
        EnsureNestedRootParent(state, side);
        if (state.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
        _startWatching(state, side);
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
