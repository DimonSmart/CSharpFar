using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal sealed class QuickViewDirectorySizeController : IDisposable
{
    private readonly DirectorySizeCalculator _calculator = new();
    private readonly Action _wakeInputLoop;
    private readonly DirectorySummaryMonitor _monitor;
    private string? _currentPath;

    public QuickViewDirectorySizeController(Action wakeInputLoop)
    {
        _wakeInputLoop = wakeInputLoop;
        _monitor = new DirectorySummaryMonitor(wakeInputLoop, RefreshMonitoredPath);
        _calculator.Completed += OnSizeCalculated;
        _calculator.Progress += OnSizeCalculated;
    }

    public DirectorySizeState? CurrentState { get; private set; }
    public DirectorySummaryMonitor Monitor => _monitor;

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
        _currentPath = item.FullPath;
        CurrentState = null;
        _calculator.Start(item.FullPath);
    }

    private void Cancel()
    {
        _calculator.Cancel();
        _monitor.Disable();
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
        if (_monitor.IsEnabled) _monitor.Disable();
        else _monitor.Enable(_currentPath);
    }

    public bool TryGetNewestMonitorTarget(out string target) => _monitor.TryGetNewestNavigableTarget(out target);

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
