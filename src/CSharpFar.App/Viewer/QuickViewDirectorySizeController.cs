using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class QuickViewDirectorySizeController : IDisposable
{
    private readonly IDirectorySizeCalculator _calculator;
    private readonly Action _wakeInputLoop;
    private readonly DirectorySummaryMonitor _monitor;
    private readonly object _lifecycleGate = new();
    private readonly Action? _beforeMonitorScanStart;
    private string? _currentPath;
    private long? _selectedChangeId;
    private IReadOnlyList<long> _visibleChangeIds = [];
    private IReadOnlyList<long> _retainedChangeIds = [];
    private long? _firstVisibleChangeId;
    private readonly RoutedScrollableList<DirectoryChange> _recentChanges = new(
        new ScrollableListState<DirectoryChange>([]),
        new UiTargetId("application.quick-view.recent-changes"),
        new UiTargetId("application.quick-view.recent-changes.scrollbar"),
        RoutedScrollableListOptions.DropdownPopup);
    private long _monitorSessionId;
    private bool _disposed;

    public QuickViewDirectorySizeController(Action wakeInputLoop)
        : this(wakeInputLoop, new DirectorySizeCalculator())
    {
    }

    internal QuickViewDirectorySizeController(
        Action wakeInputLoop,
        IDirectorySizeCalculator calculator,
        Action? beforeMonitorScanStart = null)
    {
        _wakeInputLoop = wakeInputLoop;
        _calculator = calculator;
        _beforeMonitorScanStart = beforeMonitorScanStart;
        _monitor = new DirectorySummaryMonitor(wakeInputLoop, RefreshMonitoredPath);
        _calculator.Completed += OnSizeCalculated;
        _calculator.Progress += OnSizeCalculated;
    }

    public DirectorySizeState? CurrentState { get; private set; }
    public DirectorySummaryMonitor Monitor => _monitor;
    public RoutedScrollableList<DirectoryChange> RecentChanges => _recentChanges;
    public long? SelectedMonitorChangeId => _selectedChangeId;
    public int FirstVisibleMonitorChangeIndex { get; private set; }
    public bool IsBackgroundUpdating { get; private set; }
    private long _activeScanOperationId;

    public void Update(bool quickViewEnabled, FilePanelItem? item)
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;

            if (!quickViewEnabled || item is not { IsDirectory: true, IsParentDirectory: false })
            {
                CancelIfActive();
                return;
            }

            if (_currentPath == item.FullPath)
                return;

            CancelActiveScan();
            DisableMonitor();
            _selectedChangeId = null;
            _visibleChangeIds = [];
            _retainedChangeIds = [];
            FirstVisibleMonitorChangeIndex = 0;
            _currentPath = item.FullPath;
            CurrentState = null;
            IsBackgroundUpdating = false;
            StartScan(item.FullPath, DirectoryScanProgressMode.ReportProgress);
        }
    }

    private void CancelIfActive()
    {
        if (_currentPath is not null || _monitor.IsEnabled || CurrentState is not null || _selectedChangeId is not null)
            Cancel();
    }

    private void Cancel()
    {
        CancelActiveScan();
        DisableMonitor();
        _selectedChangeId = null;
        _visibleChangeIds = [];
        _retainedChangeIds = [];
        FirstVisibleMonitorChangeIndex = 0;
        _currentPath = null;
        CurrentState = null;
        IsBackgroundUpdating = false;
    }

    private void CancelActiveScan()
    {
        Interlocked.Exchange(ref _activeScanOperationId, 0);
        _calculator.Cancel();
    }

    private void DisableMonitor()
    {
        _monitorSessionId++;
        _monitor.Disable();
    }

    private void OnSizeCalculated(DirectoryScanUpdate update)
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _currentPath != update.Path || Volatile.Read(ref _activeScanOperationId) != update.OperationId)
                return;

            if (IsBackgroundUpdating && !update.State.IsCompleted)
                return;

            CurrentState = update.State;
            if (update.State.IsCompleted)
            {
                IsBackgroundUpdating = false;
                Interlocked.Exchange(ref _activeScanOperationId, 0);
                _monitor.ScanFinished();
            }
            _wakeInputLoop();
        }
    }

    public void ToggleMonitor()
    {
        lock (_lifecycleGate)
        {
            if (_disposed || _currentPath is null)
                return;

            if (_monitor.IsEnabled)
            {
                if (IsBackgroundUpdating)
                    CancelActiveScan();
                DisableMonitor();
                IsBackgroundUpdating = false;
                _selectedChangeId = null;
                _visibleChangeIds = [];
                _retainedChangeIds = [];
                FirstVisibleMonitorChangeIndex = 0;
            }
            else
            {
                _monitorSessionId++;
                _monitor.Enable(_currentPath);
            }
        }
    }

    public bool SelectMonitorChange(long changeId)
    {
        if (!_visibleChangeIds.Contains(changeId))
            return false;
        _selectedChangeId = changeId;
        return true;
    }

    public bool MoveMonitorSelection(int offset)
    {
        if (_retainedChangeIds.Count == 0)
            return false;
        int current = _selectedChangeId is { } id
            ? _retainedChangeIds.ToList().IndexOf(id)
            : -1;
        int next = Math.Clamp(current + offset, 0, _retainedChangeIds.Count - 1);
        _selectedChangeId = _retainedChangeIds[next];
        int rows = Math.Max(1, _visibleChangeIds.Count);
        if (next < FirstVisibleMonitorChangeIndex)
            FirstVisibleMonitorChangeIndex = next;
        else if (next >= FirstVisibleMonitorChangeIndex + rows)
            FirstVisibleMonitorChangeIndex = next - rows + 1;
        _firstVisibleChangeId = _retainedChangeIds[FirstVisibleMonitorChangeIndex];
        return true;
    }

    public bool MoveMonitorSelectionByPage(int direction) =>
        MoveMonitorSelection(direction * Math.Max(1, _visibleChangeIds.Count));

    public void SetVisibleMonitorChanges(IReadOnlyList<long> changeIds, long? normalizedSelectedChangeId)
    {
        _visibleChangeIds = changeIds;
        _retainedChangeIds = changeIds;
        _selectedChangeId = normalizedSelectedChangeId;
    }

    public void SetMonitorChanges(IReadOnlyList<long> retainedChangeIds, IReadOnlyList<long> visibleChangeIds,
        long? normalizedSelectedChangeId, int firstVisibleIndex)
    {
        _retainedChangeIds = retainedChangeIds;
        _visibleChangeIds = visibleChangeIds;
        _selectedChangeId = normalizedSelectedChangeId;
        FirstVisibleMonitorChangeIndex = Math.Clamp(firstVisibleIndex, 0, Math.Max(0, retainedChangeIds.Count - Math.Max(1, visibleChangeIds.Count)));
        _firstVisibleChangeId = visibleChangeIds.FirstOrDefault();
    }

    public int GetFirstVisibleMonitorChangeIndex()
    {
        if (_firstVisibleChangeId is { } anchor)
        {
            int anchoredIndex = _monitor.GetRecentChanges().ToList().FindIndex(change => change.Id == anchor);
            if (anchoredIndex >= 0)
                return anchoredIndex;
        }
        return FirstVisibleMonitorChangeIndex;
    }

    public void SynchronizeRecentChanges()
    {
        DirectoryChange[] changes = _monitor.GetRecentChanges().ToArray();
        _recentChanges.State.ReplaceItems(changes, change => change.Id, Math.Max(1, _visibleChangeIds.Count));
        _selectedChangeId = _recentChanges.State.TryGetSelectedItem(out DirectoryChange selected) ? selected.Id : null;
    }

    public void SynchronizeRecentChangesSelection() =>
        _selectedChangeId = _recentChanges.State.TryGetSelectedItem(out DirectoryChange selected) ? selected.Id : null;

    public bool TryGetSelectedMonitorTarget(out string target)
    {
        target = string.Empty;
        return _selectedChangeId is { } id && _monitor.TryGetMonitorTarget(id, out target);
    }

    internal void RefreshMonitoredPath(string path)
    {
        long monitorSessionId;
        lock (_lifecycleGate)
        {
            if (_disposed || _currentPath != path || !_monitor.IsEnabled)
                return;
            monitorSessionId = _monitorSessionId;
        }

        _beforeMonitorScanStart?.Invoke();

        lock (_lifecycleGate)
        {
            if (_disposed || _currentPath != path || !_monitor.IsEnabled || _monitorSessionId != monitorSessionId)
                return;

            _monitor.ScanStarted();
            IsBackgroundUpdating = true;
            StartScan(path, DirectoryScanProgressMode.Silent);
        }
    }

    private void StartScan(string path, DirectoryScanProgressMode progressMode)
    {
        Interlocked.Exchange(ref _activeScanOperationId, 0);
        _calculator.Start(path, progressMode, operationId => Volatile.Write(ref _activeScanOperationId, operationId));
    }
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelActiveScan();
            DisableMonitor();
            _calculator.Dispose();
        }
    }
}
