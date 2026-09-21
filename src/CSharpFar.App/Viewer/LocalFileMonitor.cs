namespace CSharpFar.App.Viewer;

internal readonly record struct LocalFileSnapshot(
    bool Exists,
    long Length,
    DateTime LastWriteTimeUtc);

internal interface ILocalFileMonitor : IDisposable
{
    long ChangeVersion { get; }
    bool NotificationsAvailable { get; }
    void MarkDirty();
}

internal interface ILocalFileMonitorFactory
{
    ILocalFileMonitor Create(string filePath);
}

internal sealed class LocalFileMonitorFactory : ILocalFileMonitorFactory
{
    public static LocalFileMonitorFactory Instance { get; } = new();

    private LocalFileMonitorFactory()
    {
    }

    public ILocalFileMonitor Create(string filePath) => new LocalFileMonitor(filePath);
}

internal sealed class LocalFileMonitor : ILocalFileMonitor
{
    private readonly string _targetPath;
    private readonly StringComparison _pathComparison;
    private FileSystemWatcher? _watcher;
    private long _changeVersion;

    public LocalFileMonitor(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _targetPath = Path.GetFullPath(filePath);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string directory = Path.GetDirectoryName(_targetPath)
            ?? throw new ArgumentException("The file path must have a parent directory.", nameof(filePath));

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                Filter = "*",
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (IsExpectedWatcherFailure(ex))
        {
            watcher?.Dispose();
            _watcher = null;
        }
    }

    public long ChangeVersion => Volatile.Read(ref _changeVersion);

    public bool NotificationsAvailable => Volatile.Read(ref _watcher) is not null;

    public void MarkDirty() => Interlocked.Increment(ref _changeVersion);

    public static bool TryCaptureSnapshot(string filePath, out LocalFileSnapshot snapshot)
    {
        try
        {
            var info = new FileInfo(filePath);
            info.Refresh();
            snapshot = info.Exists
                ? new LocalFileSnapshot(true, info.Length, info.LastWriteTimeUtc)
                : new LocalFileSnapshot(false, 0, DateTime.MinValue);
            return true;
        }
        catch (FileNotFoundException)
        {
            snapshot = new LocalFileSnapshot(false, 0, DateTime.MinValue);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            snapshot = new LocalFileSnapshot(false, 0, DateTime.MinValue);
            return true;
        }
        catch (IOException)
        {
            snapshot = default;
            return false;
        }
    }

    internal static bool ShouldRefresh(
        LocalFileSnapshot appliedSnapshot,
        LocalFileSnapshot currentSnapshot,
        bool notificationDirty) =>
        notificationDirty || currentSnapshot != appliedSnapshot;

    public void Dispose() => DisableNotifications();

    private static bool IsExpectedWatcherFailure(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            PlatformNotSupportedException or
            NotSupportedException;

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        _ = sender;
        if (IsTargetPath(e.FullPath))
            MarkDirty();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _ = sender;
        if (IsTargetPath(e.FullPath) || IsTargetPath(e.OldFullPath))
            MarkDirty();
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _ = sender;
        _ = e;
        MarkDirty();
        DisableNotifications();
    }

    private void DisableNotifications()
    {
        FileSystemWatcher? watcher = Interlocked.Exchange(ref _watcher, null);
        if (watcher is null)
            return;

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (Exception ex) when (IsExpectedWatcherFailure(ex))
        {
        }

        watcher.Changed -= OnChanged;
        watcher.Created -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }

    private bool IsTargetPath(string path) =>
        string.Equals(Path.GetFullPath(path), _targetPath, _pathComparison);
}

internal sealed class LocalFileMonitoringSession : IDisposable
{
    private readonly string _filePath;
    private readonly ILocalFileMonitorFactory _factory;
    private ILocalFileMonitor? _monitor;
    private long _observedChangeVersion;

    public LocalFileMonitoringSession(
        string filePath,
        LocalFileSnapshot appliedSnapshot,
        ILocalFileMonitorFactory? factory = null)
    {
        _filePath = filePath;
        _factory = factory ?? LocalFileMonitorFactory.Instance;
        AppliedSnapshot = appliedSnapshot;
    }

    public LocalFileSnapshot AppliedSnapshot { get; private set; }

    public bool PendingRefresh { get; private set; }

    public bool IsActive => _monitor is not null;

    public bool NotificationsAvailable => _monitor?.NotificationsAvailable == true;

    public void Activate()
    {
        if (_monitor is not null)
            return;

        _monitor = _factory.Create(_filePath);
        _observedChangeVersion = _monitor.ChangeVersion;

        // Off intentionally collects no notifications, so every Off -> live
        // transition must re-read the current object at the target path once.
        PendingRefresh = true;
    }

    public void Deactivate()
    {
        _monitor?.Dispose();
        _monitor = null;
        _observedChangeVersion = 0;
        PendingRefresh = false;
    }

    public void MarkDirty()
    {
        PendingRefresh = true;
        _monitor?.MarkDirty();
    }

    public void DetectChanges()
    {
        bool notificationDirty = false;
        if (_monitor is not null)
        {
            long currentVersion = _monitor.ChangeVersion;
            notificationDirty = currentVersion != _observedChangeVersion;
            _observedChangeVersion = currentVersion;
        }

        if (LocalFileMonitor.TryCaptureSnapshot(_filePath, out LocalFileSnapshot snapshot))
        {
            if (LocalFileMonitor.ShouldRefresh(AppliedSnapshot, snapshot, notificationDirty))
                PendingRefresh = true;
            return;
        }

        if (notificationDirty)
            PendingRefresh = true;
    }

    public long BeginRefresh()
    {
        long version = _monitor?.ChangeVersion ?? 0;
        _observedChangeVersion = version;
        return version;
    }

    public void Commit(LocalFileSnapshot snapshot, long refreshVersion)
    {
        AppliedSnapshot = snapshot;

        if (_monitor is null)
        {
            PendingRefresh = false;
            return;
        }

        long currentVersion = _monitor.ChangeVersion;
        if (currentVersion == refreshVersion)
        {
            _observedChangeVersion = currentVersion;
            PendingRefresh = false;
            return;
        }

        // A newer notification arrived after refresh began. Do not consume it:
        // the next cycle must perform another resync.
        _observedChangeVersion = refreshVersion;
        PendingRefresh = true;
    }

    public void Dispose() => Deactivate();
}
