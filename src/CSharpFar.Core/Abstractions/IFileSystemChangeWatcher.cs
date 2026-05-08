using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFileSystemChangeWatcher : IDisposable
{
    event EventHandler<FileSystemPanelChanged>? Changed;

    PanelAutoRefreshState StartWatching(PanelWatchRequest request);
    void StopWatching(PanelSide panelSide);
}
