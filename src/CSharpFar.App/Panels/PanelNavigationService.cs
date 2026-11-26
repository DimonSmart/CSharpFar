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
    private readonly Dictionary<FilePanelState, Stack<ReturnPoint>> _returnPoints = new();
    public PanelLoadingCoordinator? Loading { get; set; }
    public Func<FilePanelState, PanelSide>? ResolveSide { get; set; }
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
        PanelLocation Location, PanelLocation ItemLocation, int CursorIndex, int ScrollOffset,
        string? DisplayTitle, string? DisplayTitleBasePath, bool ShowFullPath,
        IReadOnlyList<PanelLocation> SelectedLocations)
    {
        internal IDisposable? Lease { get; init; }
    }

    public ReturnPoint CaptureReturnPoint(FilePanelState state, FilePanelItem item) =>
        new(state.CurrentLocation, item.Location, state.CursorIndex, state.ScrollOffset,
            state.DisplayTitle, state.DisplayTitleBasePath, state.ShowCurrentItemFullPath,
            state.SelectedLocations.ToArray());

    private void PushReturnPoint(FilePanelState state, ReturnPoint point)
    {
        var lease = _sources?.AcquireUsage(point.Location.SourceId);
        if (!_returnPoints.TryGetValue(state, out var stack))
            _returnPoints[state] = stack = new Stack<ReturnPoint>();
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
        PushReturnPoint(state, point);
        EnsureNestedRootParent(state, side);
    }

    public void ClearNavigation(FilePanelState state)
    {
        if (!_returnPoints.Remove(state, out var stack))
            return;
        while (stack.TryPop(out var frame))
            frame.Lease?.Dispose();
    }

    public void Navigate(FilePanelState state, PanelLocation location, bool preserveOnError, Action<bool> completed, bool requireExistingLocalDirectory = false, Action? abandoned = null)
    {
        if (Loading is { } loading)
            loading.Navigate(state, location, _panelOptions(), preserveOnError, completed, requireExistingLocalDirectory, abandoned);
        else
            completed(_controller.TryLoadLocation(state, location, _panelOptions(), preserveOnError));
    }

    private PanelSide CurrentSide(FilePanelState state, PanelSide fallback) => ResolveSide?.Invoke(state) ?? fallback;

    private void TryReturn(FilePanelState state, PanelSide side)
    {
        if (!_returnPoints.TryGetValue(state, out var stack) || !stack.TryPeek(out var frame))
            return;
        Navigate(state, frame.Location, preserveOnError: true, loaded =>
        {
            if (!loaded) return;
            PanelSide currentSide = CurrentSide(state, side);
            _sources?.RetainSource(frame.Location.SourceId);
            stack.Pop();
            frame.Lease?.Dispose();
            if (stack.Count == 0)
                _returnPoints.Remove(state);
            _controller.SetSourceDisplay(state, frame.DisplayTitle, frame.ShowFullPath, frame.DisplayTitleBasePath);
            var available = state.Items.Where(item => !item.IsParentDirectory)
                .Select(item => item.Location).ToHashSet(state.SelectedLocations.Comparer);
            state.SelectedLocations.UnionWith(frame.SelectedLocations.Where(available.Contains));
            _controller.RefreshSelectionSummary(state);
            if (!_controller.SetCursorByLocation(state, frame.ItemLocation, _visibleRows(currentSide)))
                _controller.SetCursorTo(state, frame.CursorIndex, _visibleRows(currentSide));
            state.ScrollOffset = frame.ScrollOffset;
            _controller.NormalizeCursor(state, _visibleRows(currentSide));
            EnsureNestedRootParent(state, currentSide);
            _startWatching(state, currentSide);
        });
    }

    private bool CanReturnFromRoot(FilePanelState state, PanelSide side)
    {
        if (!_returnPoints.TryGetValue(state, out var stack) || stack.Count == 0)
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
        if (item.IsParentDirectory)
            NavigateParent(state, side);
        else
            Navigate(state, item.Location, preserveOnError: false, loaded => AfterNavigation(state, side, loaded));
    }

    public void TryGoUp(FilePanelState state, PanelSide side)
    {
        if (!HasCapability(state, PanelProviderCapabilities.Enumerate))
            return;

        _closeQuickSearchForPanel(side);
        if (CanReturnFromRoot(state, side))
        {
            TryReturn(state, side);
            return;
        }
        NavigateParent(state, side);
    }

    private void NavigateParent(FilePanelState state, PanelSide side)
    {
        if (Loading is null)
        {
            AfterNavigation(state, side, _controller.TryGoToParent(state, _visibleRows(side), _panelOptions()));
            return;
        }
        PanelLocation child = state.CurrentLocation;
        PanelLocation? parent = state.SourceId == PanelSourceId.Local
            ? _controller.GetParentLocation(child)
            : state.Items.FirstOrDefault(item => item.IsParentDirectory)?.Location;
        if (parent is not { } target || state.SelectedLocations.Comparer.Equals(child, target))
            return;
        Navigate(state, target, preserveOnError: false, loaded =>
        {
            if (loaded)
                _controller.SetCursorByLocation(state, child, _visibleRows(CurrentSide(state, side)));
            AfterNavigation(state, side, loaded);
        });
    }

    private void AfterNavigation(FilePanelState state, PanelSide side, bool loaded)
    {
        if (!loaded) return;
        side = CurrentSide(state, side);
        EnsureNestedRootParent(state, side);
        if (state.SourceId == PanelSourceId.Local)
            _history.AddDirectory(new DirectoryHistoryItem { Path = state.CurrentDirectory });
        _startWatching(state, side);
    }

    private static bool HasCapability(FilePanelState state, PanelProviderCapabilities capability) =>
        (state.ProviderCapabilities & capability) == capability;
}
