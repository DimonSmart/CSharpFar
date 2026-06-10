using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal sealed class QuickViewDirectorySizeController : IDisposable
{
    private readonly DirectorySizeCalculator _calculator = new();
    private readonly Action _wakeInputLoop;
    private string? _currentPath;

    public QuickViewDirectorySizeController(Action wakeInputLoop)
    {
        _wakeInputLoop = wakeInputLoop;
        _calculator.Completed += OnSizeCalculated;
        _calculator.Progress += OnSizeCalculated;
    }

    public DirectorySizeState? CurrentState { get; private set; }

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

        _currentPath = item.FullPath;
        CurrentState = null;
        _calculator.Start(item.FullPath);
    }

    private void Cancel()
    {
        _calculator.Cancel();
        _currentPath = null;
        CurrentState = null;
    }

    private void OnSizeCalculated(string path, DirectorySizeState state)
    {
        if (_currentPath != path)
            return;

        CurrentState = state;
        _wakeInputLoop();
    }

    public void Dispose() => _calculator.Dispose();
}
