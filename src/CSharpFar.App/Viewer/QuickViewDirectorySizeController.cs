using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal sealed class QuickViewDirectorySizeController : IDisposable
{
    private readonly DirectorySizeCalculator _calculator = new();
    private readonly Action _wakeInputLoop;
    private readonly DirectorySummaryMonitor _monitor;
    private string? _currentPath;
    private long? _selectedChangeId;

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
            Cancel();
            return;
        }

        if (item is not { IsDirectory: true, IsParentDirectory: false })
        {
            Cancel();
            return;
        }

        if (_currentPath == item.FullPath)
            return;

        _monitor.Disable();
        _selectedChangeId = null;
        _currentPath = item.FullPath;
        CurrentState = null;
        _calculator.Start(item.FullPath);
    }

    private void Cancel()
    {
        _calculator.Cancel();
        _monitor.Disable();
        _selectedChangeId = null;
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
        }
        else _monitor.Enable(_currentPath);
    }

    public bool SelectMonitorChange(long changeId)
    {
        if (!_monitor.GetRecentChanges().Any(change => change.Id == changeId))
            return false;
        _selectedChangeId = changeId;
        return true;
    }

    public bool MoveMonitorSelection(int offset)
    {
        IReadOnlyList<DirectoryChange> changes = _monitor.GetRecentChanges();
        if (changes.Count == 0)
            return false;
        int current = _selectedChangeId is { } id
            ? changes.ToList().FindIndex(change => change.Id == id)
            : -1;
        int next = Math.Clamp(current + offset, 0, changes.Count - 1);
        _selectedChangeId = changes[next].Id;
        return true;
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
