using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Viewer;

internal enum DirectoryChangeKind { Created, Changed, Deleted, Renamed }

internal sealed record DirectoryChange(
    DirectoryChangeKind Kind, string RelativePath, string? OldRelativePath, string FullPath, DateTime Timestamp);

/// <summary>Owns the recursive watcher and its bounded, best-effort event history.</summary>
internal sealed class DirectorySummaryMonitor : IDisposable
{
    private const int DebounceMilliseconds = 400;
    private const int MinimumScanIntervalMilliseconds = 1000;
    private const int MaxRecentChanges = 10;
    private readonly object _gate = new();
    private readonly Action _wake;
    private readonly Action<string> _rescan;
    private readonly List<DirectoryChange> _changes = [];
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _delay;
    private string? _root;
    private bool _scanning;
    private bool _dirty;
    private long _version;
    private long _lastScanTick;
    private bool _disposed;

    public DirectorySummaryMonitor(Action wake, Action<string> rescan)
    {
        _wake = wake;
        _rescan = rescan;
    }

    public bool IsEnabled => _watcher is not null;
    public IReadOnlyList<DirectoryChange> RecentChanges => _changes;

    public bool TryGetNewestNavigableTarget(out string target)
    {
        target = string.Empty;
        DirectoryChange? change;
        lock (_gate)
            change = _changes.FirstOrDefault(c => c.Kind != DirectoryChangeKind.Deleted);
        if (change is null || !File.Exists(change.FullPath) && !Directory.Exists(change.FullPath))
            return false;
        target = change.FullPath;
        return true;
    }

    public void Enable(string root)
    {
        Disable();
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = false,
            };
            watcher.Created += (_, e) => Record(DirectoryChangeKind.Created, e.FullPath, null);
            watcher.Changed += (_, e) => Record(DirectoryChangeKind.Changed, e.FullPath, null);
            watcher.Deleted += (_, e) => Record(DirectoryChangeKind.Deleted, e.FullPath, null);
            watcher.Renamed += (_, e) => Record(DirectoryChangeKind.Renamed, e.FullPath, e.OldFullPath);
            watcher.Error += (_, _) => RequestRefresh();
            _root = Path.GetFullPath(root);
            _watcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            Disable();
        }
        _wake();
    }

    public void Disable()
    {
        var watcher = Interlocked.Exchange(ref _watcher, null);
        watcher?.Dispose();
        _delay?.Cancel();
        _delay?.Dispose();
        _delay = null;
        _root = null;
        _changes.Clear();
        _dirty = false;
        _scanning = false;
        _wake();
    }

    public void ScanStarted() { lock (_gate) _scanning = true; }
    public void ScanFinished()
    {
        bool again;
        lock (_gate) { _scanning = false; again = _dirty; _dirty = false; }
        if (again) ScheduleRefresh();
    }

    private void Record(DirectoryChangeKind kind, string fullPath, string? oldFullPath)
    {
        string? root = _root;
        if (_disposed || root is null) return;
        string relative = Relative(root, fullPath);
        string? oldRelative = oldFullPath is null ? null : Relative(root, oldFullPath);
        lock (_gate)
        {
            // A short same-kind/same-path burst is not useful to users.
            if (_changes.FirstOrDefault() is { } last && last.Kind == kind && last.FullPath == fullPath &&
                (DateTime.UtcNow - last.Timestamp).TotalMilliseconds < DebounceMilliseconds)
                return;
            _changes.Insert(0, new DirectoryChange(kind, relative, oldRelative, fullPath, DateTime.Now));
            if (_changes.Count > MaxRecentChanges) _changes.RemoveRange(MaxRecentChanges, _changes.Count - MaxRecentChanges);
            _version++;
            _dirty = true;
        }
        ScheduleRefresh();
        _wake();
    }

    private void RequestRefresh()
    {
        lock (_gate) { _version++; _dirty = true; }
        ScheduleRefresh();
        _wake();
    }

    private void ScheduleRefresh()
    {
        if (_watcher is null || _disposed) return;
        var old = Interlocked.Exchange(ref _delay, new CancellationTokenSource());
        old?.Cancel(); old?.Dispose();
        CancellationToken token = _delay.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                long wait = Math.Max(DebounceMilliseconds, MinimumScanIntervalMilliseconds - (Environment.TickCount64 - _lastScanTick));
                await Task.Delay((int)wait, token);
                string? path;
                lock (_gate)
                {
                    if (_scanning || !_dirty) return;
                    _dirty = false; _scanning = true; path = _root; _lastScanTick = Environment.TickCount64;
                }
                if (path is not null) _rescan(path);
            }
            catch (OperationCanceledException) { }
        });
    }

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    public void Dispose() { _disposed = true; Disable(); }
}
