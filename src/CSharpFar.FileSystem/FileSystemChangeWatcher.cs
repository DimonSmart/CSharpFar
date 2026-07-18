using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>
/// Watches panel directories for changes using <see cref="FileSystemWatcher"/>.
/// Debounces rapid events to 400 ms. Does not mutate panel state;
/// raises <see cref="Changed"/> for the Application to consume.
/// </summary>
public sealed class FileSystemChangeWatcher : IFileSystemChangeWatcher
{
    private sealed class PanelWatch : IDisposable
    {
        public readonly FileSystemWatcher Watcher;
        public readonly PanelSide Side;
        public readonly string DirectoryPath;
        private readonly object _lock = new();
        private CancellationTokenSource? _debounceCts;

        public event EventHandler<FileSystemPanelChanged>? Changed;

        public PanelWatch(FileSystemWatcher watcher, PanelSide side, string path)
        {
            Watcher = watcher;
            Side = side;
            DirectoryPath = path;
        }

        public void OnEvent(FileSystemChangeKind kind)
        {
            CancellationTokenSource newCts;
            lock (_lock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = newCts = new CancellationTokenSource();
            }

            _ = Task.Delay(400, newCts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                    Changed?.Invoke(this, new FileSystemPanelChanged(Side, DirectoryPath, kind));
            }, TaskScheduler.Default);
        }

        public void Dispose()
        {
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();
            lock (_lock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }
        }
    }

    private PanelWatch? _left;
    private PanelWatch? _right;
    private readonly object _lock = new();

    public event EventHandler<FileSystemPanelChanged>? Changed;

    public PanelAutoRefreshState StartWatching(PanelWatchRequest request)
    {
        StopWatching(request.PanelSide);

        if (request.Options.DisableIfObjectCountExceeds > 0 &&
            request.ObjectCount > request.Options.DisableIfObjectCountExceeds)
        {
            return new PanelAutoRefreshState { IsWatching = false, DisabledByObjectCount = true };
        }

        if (request.IsNetworkDrive && !request.Options.NetworkDrivesAutoRefresh)
        {
            return new PanelAutoRefreshState { IsWatching = false, DisabledForNetworkDrive = true };
        }

        try
        {
            var fw = new FileSystemWatcher(request.DirectoryPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
            };

            var pw = new PanelWatch(fw, request.PanelSide, request.DirectoryPath);
            pw.Changed += (_, e) => Changed?.Invoke(this, e);

            fw.Created += (_, e) => pw.OnEvent(MapKind(e.ChangeType));
            fw.Deleted += (_, e) => pw.OnEvent(MapKind(e.ChangeType));
            fw.Changed += (_, e) => pw.OnEvent(MapKind(e.ChangeType));
            fw.Renamed += (_, _) => pw.OnEvent(FileSystemChangeKind.Renamed);
            fw.Error += (_, _) => pw.OnEvent(FileSystemChangeKind.Overflow);

            lock (_lock)
            {
                if (request.PanelSide == PanelSide.Left) _left = pw;
                else _right = pw;
            }

            fw.EnableRaisingEvents = true;
            return new PanelAutoRefreshState { IsWatching = true };
        }
        catch (Exception ex)
        {
            return new PanelAutoRefreshState { IsWatching = false, LastError = ex.Message };
        }
    }

    public void StopWatching(PanelSide panelSide)
    {
        PanelWatch? pw;
        lock (_lock)
        {
            if (panelSide == PanelSide.Left) { pw = _left; _left = null; }
            else { pw = _right; _right = null; }
        }
        pw?.Dispose();
    }

    public void Dispose()
    {
        StopWatching(PanelSide.Left);
        StopWatching(PanelSide.Right);
    }

    private static FileSystemChangeKind MapKind(WatcherChangeTypes ct) => ct switch
    {
        WatcherChangeTypes.Created => FileSystemChangeKind.Created,
        WatcherChangeTypes.Deleted => FileSystemChangeKind.Deleted,
        WatcherChangeTypes.Changed => FileSystemChangeKind.Changed,
        WatcherChangeTypes.Renamed => FileSystemChangeKind.Renamed,
        _ => FileSystemChangeKind.Unknown,
    };
}
