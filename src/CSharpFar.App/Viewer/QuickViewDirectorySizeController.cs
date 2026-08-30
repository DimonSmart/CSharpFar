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
        _calculator.Start(item.FullPath);
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
    }

    private void OnSizeCalculated(string path, DirectorySizeState state)
    {
        if (_currentPath != path)
            return;

        CurrentState = state;
        if (state.IsCompleted)
            _monitor.ScanFinished();
        _wakeInputLoop();
    }

    public void ToggleMonitor()
    {
        if (_currentPath is null) return;
        if (_monitor.IsEnabled)
        {
            _monitor.Disable();
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

    public bool TryGetSelectedMonitorTarget(out string target)
    {
        target = string.Empty;
        return _selectedChangeId is { } id && _monitor.TryGetMonitorTarget(id, out target);
    }

    private void RefreshMonitoredPath(string path)
    {
        if (_currentPath != path || !_monitor.IsEnabled) return;
        _monitor.ScanStarted();
        _calculator.Start(path);
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _calculator.Dispose();
    }
}
