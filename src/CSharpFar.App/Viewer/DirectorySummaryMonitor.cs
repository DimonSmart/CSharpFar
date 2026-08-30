using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.App.Viewer;

internal enum DirectoryChangeKind { Created, Changed, Deleted, Renamed }

internal sealed record DirectoryChange(
    long Id, DirectoryChangeKind Kind, string RelativePath, string? OldRelativePath, string FullPath, DateTimeOffset Timestamp, long Tick,
    int RepeatCount);

/// <summary>Owns the recursive watcher and its bounded, best-effort event history.</summary>
internal sealed class DirectorySummaryMonitor : IDisposable
{
    private const int DebounceMilliseconds = 400;
    private const int MinimumScanIntervalMilliseconds = 1000;
    private const int MaxRecentHistory = 256;
    private readonly object _gate = new();
    private readonly Action _wake;
    private readonly Action<string> _rescan;
    private readonly List<DirectoryChange> _changes = [];
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _delay;
    private string? _root;
    private bool _scanning;
    private long _version;
    private long _scanVersion;
    private long _nextChangeId;
    private long _lastScanTick;
    private long _generation;
    private bool _disposed;

    public DirectorySummaryMonitor(Action wake, Action<string> rescan)
    {
        _wake = wake;
        _rescan = rescan;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _watcher is not null;
        }
    }

    internal long CurrentGeneration => Interlocked.Read(ref _generation);
    public IReadOnlyList<DirectoryChange> GetRecentChanges()
    {
        lock (_gate)
            return _changes.ToArray();
    }

    public bool TryGetMonitorTarget(long changeId, out string target)
    {
        target = string.Empty;
        DirectoryChange? change;
        lock (_gate)
            change = _changes.FirstOrDefault(c => c.Id == changeId);
        if (change is null || change.Kind == DirectoryChangeKind.Deleted ||
            !File.Exists(change.FullPath) && !Directory.Exists(change.FullPath))
            return false;
        target = change.FullPath;
        return true;
    }

    public void Enable(string root)
    {
        Disable();
        try
        {
            string normalizedRoot = Path.GetFullPath(root);
            long generation = Interlocked.Increment(ref _generation);
            var watcher = new FileSystemWatcher(normalizedRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = false,
            };
            watcher.Created += (_, e) => RecordChange(generation, normalizedRoot, DirectoryChangeKind.Created, e.FullPath, null);
            watcher.Changed += (_, e) => RecordChange(generation, normalizedRoot, DirectoryChangeKind.Changed, e.FullPath, null);
            watcher.Deleted += (_, e) => RecordChange(generation, normalizedRoot, DirectoryChangeKind.Deleted, e.FullPath, null);
            watcher.Renamed += (_, e) => RecordChange(generation, normalizedRoot, DirectoryChangeKind.Renamed, e.FullPath, e.OldFullPath);
            watcher.Error += (_, _) => RequestRefresh(generation, normalizedRoot);
            lock (_gate)
            {
                _root = normalizedRoot;
                _watcher = watcher;
            }
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            Disable();
        }
    }

    public void Disable()
    {
        Interlocked.Increment(ref _generation);
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
            _root = null;
            _changes.Clear();
            _scanning = false;
            _version++;
        }
        watcher?.Dispose();
        var delay = Interlocked.Exchange(ref _delay, null);
        delay?.Cancel();
        delay?.Dispose();
    }

    public void ScanStarted()
    {
        lock (_gate)
        {
            _scanning = true;
            _scanVersion = _version;
        }
    }
    public void ScanFinished()
    {
        long generation;
        string? root;
        bool again;
        lock (_gate)
        {
            _scanning = false;
            again = _version != _scanVersion;
            generation = _generation;
            root = _root;
        }
        if (again && root is not null) ScheduleRefresh(generation, root);
    }

    internal void RecordChange(DirectoryChangeKind kind, string fullPath, string? oldFullPath)
        => RecordChange(kind, fullPath, oldFullPath, DateTimeOffset.UtcNow, Environment.TickCount64);

    internal void RecordChange(
        DirectoryChangeKind kind,
        string fullPath,
        string? oldFullPath,
        DateTimeOffset timestamp,
        long tick)
    {
        long generation;
        string? root;
        lock (_gate)
        {
            generation = _generation;
            root = _root;
        }
        if (root is not null)
            RecordChange(generation, root, kind, fullPath, oldFullPath, timestamp, tick);
    }

    internal void RecordChange(long generation, string root, DirectoryChangeKind kind, string fullPath, string? oldFullPath)
        => RecordChange(generation, root, kind, fullPath, oldFullPath, DateTimeOffset.UtcNow, Environment.TickCount64);

    private void RecordChange(
        long generation,
        string root,
        DirectoryChangeKind kind,
        string fullPath,
        string? oldFullPath,
        DateTimeOffset timestamp,
        long tick)
    {
        if (!IsCurrentSession(generation, root)) return;
        string relative = Relative(root, fullPath);
        string? oldRelative = oldFullPath is null ? null : Relative(root, oldFullPath);
        lock (_gate)
        {
            if (!IsCurrentSessionUnsafe(generation, root)) return;
            int existingIndex = kind == DirectoryChangeKind.Changed
                ? _changes.FindIndex(change => change.Kind == DirectoryChangeKind.Changed && LocalFileSystemPathComparer.Current.Equals(change.FullPath, fullPath))
                : -1;
            if (existingIndex >= 0)
            {
                DirectoryChange existing = _changes[existingIndex];
                _changes.RemoveAt(existingIndex);
                _changes.Insert(0, existing with
                {
                    RelativePath = relative,
                    FullPath = fullPath,
                    Timestamp = timestamp,
                    Tick = tick,
                    RepeatCount = checked(existing.RepeatCount + 1),
                });
            }
            else
                _changes.Insert(0, new DirectoryChange(++_nextChangeId, kind, relative, oldRelative, fullPath, timestamp, tick, RepeatCount: 1));
            if (_changes.Count > MaxRecentHistory) _changes.RemoveRange(MaxRecentHistory, _changes.Count - MaxRecentHistory);
            _version++;
        }
        ScheduleRefresh(generation, root);
        _wake();
    }

    private void RequestRefresh(long generation, string root)
    {
        lock (_gate)
        {
            if (!IsCurrentSessionUnsafe(generation, root)) return;
            _version++;
        }
        ScheduleRefresh(generation, root);
        _wake();
    }

    private void ScheduleRefresh(long generation, string root)
    {
        if (!IsCurrentSession(generation, root)) return;
        var next = new CancellationTokenSource();
        CancellationToken token = next.Token;
        var old = Interlocked.Exchange(ref _delay, next);
        old?.Cancel(); old?.Dispose();
        _ = Task.Run(async () =>
        {
            try
            {
                long wait = Math.Max(DebounceMilliseconds, MinimumScanIntervalMilliseconds - (Environment.TickCount64 - _lastScanTick));
                await Task.Delay((int)wait, token);
                string? path;
                lock (_gate)
                {
                    if (!ReferenceEquals(Volatile.Read(ref _delay), next) || !IsCurrentSessionUnsafe(generation, root) || _scanning) return;
                    _scanning = true; _scanVersion = _version; path = root; _lastScanTick = Environment.TickCount64;
                }
                if (path is not null && IsCurrentSession(generation, root)) _rescan(path);
            }
            catch (OperationCanceledException) { }
        });
    }

    private bool IsCurrentSession(long generation, string root)
    {
        lock (_gate)
            return IsCurrentSessionUnsafe(generation, root);
    }

    private bool IsCurrentSessionUnsafe(long generation, string root) =>
        !_disposed && _generation == generation && _watcher is not null && LocalFileSystemPathComparer.Current.Equals(_root, root);

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    public void Dispose()
    {
        _disposed = true;
        Disable();
    }
}
