namespace CSharpFar.App.Viewer;

internal readonly record struct LocalFileSnapshot(
    bool Exists,
    long Length,
    DateTime LastWriteTimeUtc);

internal sealed class LocalFileMonitor : IDisposable
{
    private readonly string _targetPath;
    private readonly StringComparison _pathComparison;
    private readonly FileSystemWatcher _watcher;
    private int _dirty;

    public LocalFileMonitor(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _targetPath = Path.GetFullPath(filePath);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string directory = Path.GetDirectoryName(_targetPath)
            ?? throw new ArgumentException("The file path must have a parent directory.", nameof(filePath));

        _watcher = new FileSystemWatcher(directory)
        {
            Filter = "*",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName |
                           NotifyFilters.LastWrite |
                           NotifyFilters.Size |
                           NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    public void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    public bool TakeDirty() => Interlocked.Exchange(ref _dirty, 0) != 0;

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
        bool watcherDirty) =>
        watcherDirty || currentSnapshot != appliedSnapshot;

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnChanged;
        _watcher.Created -= OnChanged;
        _watcher.Deleted -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

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
    }

    private bool IsTargetPath(string path) =>
        string.Equals(Path.GetFullPath(path), _targetPath, _pathComparison);
}
