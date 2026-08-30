using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal sealed class QuickViewDirectorySizeController : IDisposable
{
    private readonly DirectorySizeCalculator _calculator = new();
    private readonly Action _wakeInputLoop;
    private readonly DirectorySummaryMonitor _monitor;
    private string? _currentPath;
    private long? _selectedChangeId;
    private IReadOnlyList<long> _visibleChangeIds = [];

    public QuickViewDirectorySizeController(Action wakeInputLoop)
    {
        _wakeInputLoop = wakeInputLoop;
        _monitor = new DirectorySummaryMonitor(wakeInputLoop, RefreshMonitoredPath);
        _calculator.Completed += OnSizeCalculated;
        _calculator.Progress += OnSizeCalculated;
    }

    public DirectorySizeState? CurrentState { get; private set; }
    public DirectorySummaryMonitor Monitor => _monitor;
    public long? SelectedMonitorChangeId => _selectedChangeId;
    public bool IsBackgroundUpdating { get; private set; }
    private long _activeScanOperationId;

    public void Update(bool quickViewEnabled, FilePanelItem? item)
    {
        if (!quickViewEnabled)
        {
            CancelIfActive();
            return;
        }

        if (item is not { IsDirectory: true, IsParentDirectory: false })
        {
            CancelIfActive();
            return;
        }

        if (_currentPath == item.FullPath)
            return;

        _monitor.Disable();
        _selectedChangeId = null;
        _visibleChangeIds = [];
        _currentPath = item.FullPath;
        CurrentState = null;
        IsBackgroundUpdating = false;
        StartScan(item.FullPath, DirectoryScanProgressMode.ReportProgress);
    }

    private void CancelIfActive()
    {
        if (_currentPath is not null || _monitor.IsEnabled || CurrentState is not null || _selectedChangeId is not null)
            Cancel();
    }

    private void Cancel()
    {
        _calculator.Cancel();
        _monitor.Disable();
        _selectedChangeId = null;
        _visibleChangeIds = [];
        _currentPath = null;
        CurrentState = null;
        IsBackgroundUpdating = false;
        _activeScanOperationId = 0;
    }

    private void OnSizeCalculated(DirectoryScanUpdate update)
    {
        if (_currentPath != update.Path || _activeScanOperationId != update.OperationId)
            return;

        CurrentState = update.State;
        if (update.State.IsCompleted)
        {
            IsBackgroundUpdating = false;
            _monitor.ScanFinished();
        }
        _wakeInputLoop();
    }

    public void ToggleMonitor()
    {
        if (_currentPath is null) return;
        if (_monitor.IsEnabled)
        {
            if (IsBackgroundUpdating)
                _calculator.Cancel();
            _monitor.Disable();
            IsBackgroundUpdating = false;
            _selectedChangeId = null;
            _visibleChangeIds = [];
        }
        else _monitor.Enable(_currentPath);
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
        if (_visibleChangeIds.Count == 0)
            return false;
        int current = _selectedChangeId is { } id
            ? _visibleChangeIds.ToList().IndexOf(id)
            : -1;
        int next = Math.Clamp(current + offset, 0, _visibleChangeIds.Count - 1);
        _selectedChangeId = _visibleChangeIds[next];
        return true;
    }

    public void SetVisibleMonitorChanges(IReadOnlyList<long> changeIds)
    {
        _visibleChangeIds = changeIds;
        if (_selectedChangeId is not null && !_visibleChangeIds.Contains(_selectedChangeId.Value))
            _selectedChangeId = _visibleChangeIds.Count > 0 ? _visibleChangeIds[^1] : null;
    }

    public long? NormalizeVisibleMonitorChanges(IReadOnlyList<long> changeIds)
    {
        SetVisibleMonitorChanges(changeIds);
        return _selectedChangeId;
    }

    public bool TryGetSelectedMonitorTarget(out string target)
    {
        target = string.Empty;
        return _selectedChangeId is { } id && _monitor.TryGetMonitorTarget(id, out target);
    }

    private void RefreshMonitoredPath(string path)
    {
        if (_currentPath != path || !_monitor.IsEnabled) return;
        _monitor.ScanStarted();
        IsBackgroundUpdating = true;
        StartScan(path, DirectoryScanProgressMode.Silent);
    }

    private void StartScan(string path, DirectoryScanProgressMode progressMode) =>
        _calculator.Start(path, progressMode, operationId => _activeScanOperationId = operationId);

    public void Dispose()
    {
        _monitor.Dispose();
        _calculator.Dispose();
    }
}
