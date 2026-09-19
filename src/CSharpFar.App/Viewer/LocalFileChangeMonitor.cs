namespace CSharpFar.App.Viewer;

internal enum LocalFileChangeKind
{
    None,
    Unavailable,
    Missing,
    Append,
    Reload,
}

internal readonly record struct LocalFileSnapshot(
    bool IsAvailable,
    bool Exists,
    long Length,
    long LastWriteTimeUtcTicks,
    long CreationTimeUtcTicks)
{
    public static LocalFileSnapshot Unavailable => new(false, false, 0, 0, 0);
    public static LocalFileSnapshot Missing => new(true, false, 0, 0, 0);
}

internal readonly record struct LocalFileChange(
    LocalFileChangeKind Kind,
    LocalFileSnapshot Previous,
    LocalFileSnapshot Current);

internal sealed class LocalFileChangeMonitor : IDisposable
{
    private readonly string _filePath;
    private FileSystemWatcher? _watcher;
    private LocalFileSnapshot _accepted;
    private long _eventGeneration;
    private long _structuralGeneration;
    private long _acceptedEventGeneration;
    private long _acceptedStructuralGeneration;

    public LocalFileChangeMonitor(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _accepted = ReadSnapshot(_filePath);

        string? directory = Path.GetDirectoryName(_filePath);
        string fileName = Path.GetFileName(_filePath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            return;

        try
        {
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.Size |
                               NotifyFilters.LastWrite |
                               NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                EnableRaisingEvents = false,
            };
            _watcher.Changed += OnContentChanged;
            _watcher.Created += OnStructuralChanged;
            _watcher.Deleted += OnStructuralChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public LocalFileChange Check()
    {
        LocalFileSnapshot current = ReadSnapshot(_filePath);
        if (!current.IsAvailable)
            return new LocalFileChange(LocalFileChangeKind.Unavailable, _accepted, current);

        long eventGeneration = Volatile.Read(ref _eventGeneration);
        long structuralGeneration = Volatile.Read(ref _structuralGeneration);
        bool eventChanged = eventGeneration != _acceptedEventGeneration;
        bool structuralChanged = structuralGeneration != _acceptedStructuralGeneration;
        bool metadataChanged = current != _accepted;

        if (!eventChanged && !metadataChanged)
            return new LocalFileChange(LocalFileChangeKind.None, _accepted, current);

        LocalFileChangeKind kind;
        if (!current.Exists)
        {
            kind = LocalFileChangeKind.Missing;
        }
        else if (!_accepted.Exists)
        {
            kind = LocalFileChangeKind.Reload;
        }
        else if (!structuralChanged &&
                 current.CreationTimeUtcTicks == _accepted.CreationTimeUtcTicks &&
                 current.Length > _accepted.Length)
        {
            kind = LocalFileChangeKind.Append;
        }
        else
        {
            kind = LocalFileChangeKind.Reload;
        }

        return new LocalFileChange(kind, _accepted, current);
    }

    public void Accept(LocalFileChange change)
    {
        if (!change.Current.IsAvailable)
            return;

        _accepted = change.Current;
        _acceptedEventGeneration = Volatile.Read(ref _eventGeneration);
        _acceptedStructuralGeneration = Volatile.Read(ref _structuralGeneration);
    }

    public void Dispose() => _watcher?.Dispose();

    private void OnContentChanged(object sender, FileSystemEventArgs e) =>
        Interlocked.Increment(ref _eventGeneration);

    private void OnStructuralChanged(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventGeneration);
        Interlocked.Increment(ref _structuralGeneration);
    }

    private void OnRenamed(object sender, RenamedEventArgs e) => OnStructuralChanged(sender, e);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Interlocked.Increment(ref _eventGeneration);
        Interlocked.Increment(ref _structuralGeneration);
    }

    private static LocalFileSnapshot ReadSnapshot(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            info.Refresh();
            if (!info.Exists)
                return LocalFileSnapshot.Missing;

            return new LocalFileSnapshot(
                IsAvailable: true,
                Exists: true,
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                info.CreationTimeUtc.Ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LocalFileSnapshot.Unavailable;
        }
    }
}
