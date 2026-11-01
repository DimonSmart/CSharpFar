using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests.Infrastructure;

/// <summary>An entirely in-memory transactional file source used only by infrastructure tests.</summary>
internal class MemoryFilePanelView : IFilePanelSource
{
    private readonly object _gate = new();
    protected Dictionary<string, byte[]?> Entries;

    public MemoryFilePanelView(PanelSourceId id, Dictionary<string, byte[]?>? entries = null)
    {
        SourceId = id;
        Entries = entries ?? new Dictionary<string, byte[]?>(StringComparer.Ordinal) { ["/"] = null };
    }

    public PanelSourceId SourceId { get; }
    public string DisplayName => "In-memory container";
    public PanelProviderCapabilities Capabilities =>
        PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.OpenRead |
        PanelProviderCapabilities.OpenWrite | PanelProviderCapabilities.CreateFile |
        PanelProviderCapabilities.CreateDirectory | PanelProviderCapabilities.Delete |
        PanelProviderCapabilities.Rename | PanelProviderCapabilities.CopyFrom |
        PanelProviderCapabilities.CopyTo | PanelProviderCapabilities.Edit;
    public IReadOnlyCollection<char> PathSeparators => ['/'];
    public bool FailWrite { get; set; }

    public string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (var component in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".") continue;
            if (component == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(component);
        }
        return "/" + string.Join('/', parts);
    }

    public bool IsRootPath(string path) => NormalizePath(path) == "/";
    public string? GetParentPath(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized == "/") return null;
        int index = normalized.LastIndexOf('/');
        return index == 0 ? "/" : normalized[..index];
    }

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!Entries.TryGetValue(path, out var directory) || directory is not null)
                throw new DirectoryNotFoundException(path);
            return Entries.Where(entry => entry.Key != path && GetParentPath(entry.Key) == path)
                .Select(entry => ToItem(entry.Key, entry.Value)).OrderBy(item => item.Name).ToArray();
        }
    }

    public FilePanelItem? GetItem(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
            return Entries.TryGetValue(path, out var value) ? ToItem(path, value) : null;
    }

    private FilePanelItem ToItem(string path, byte[]? content) => new()
    {
        SourceId = SourceId, FullPath = path, Name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..],
        IsDirectory = content is null, Size = content?.Length,
        Attributes = content is null ? FileAttributes.Directory : FileAttributes.Normal,
        LastWriteTime = DateTime.UnixEpoch,
    };

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!Entries.TryGetValue(path, out var bytes) || bytes is null)
                throw new FileNotFoundException(path);
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }

    public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!Entries.ContainsKey(GetParentPath(path) ?? "/"))
                throw new DirectoryNotFoundException(path);
            if (!overwrite && Entries.ContainsKey(path))
                throw new IOException("File exists.");
        }
        return Task.FromResult<Stream>(new WriteBackStream(bytes =>
        {
            if (FailWrite) throw new IOException("Simulated stream completion failure.");
            lock (_gate) Entries[path] = bytes;
        }));
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!Entries.ContainsKey(GetParentPath(path) ?? "/"))
                throw new DirectoryNotFoundException(path);
            if (Entries.TryGetValue(path, out var existing) && existing is not null)
                throw new IOException("A file already exists.");
            Entries[path] = null;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        lock (_gate)
        {
            if (!recursive && Entries.Keys.Any(key => key.StartsWith(path + "/", StringComparison.Ordinal)))
                throw new IOException("Directory is not empty.");
            foreach (var key in Entries.Keys.Where(key => key == path || key.StartsWith(path + "/", StringComparison.Ordinal)).ToArray())
                Entries.Remove(key);
        }
        return Task.CompletedTask;
    }

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = NormalizePath(path);
        newPath = NormalizePath(newPath);
        lock (_gate)
        {
            foreach (var key in Entries.Keys.Where(key => key == path || key.StartsWith(path + "/", StringComparison.Ordinal)).ToArray())
            {
                var bytes = Entries[key];
                Entries.Remove(key);
                Entries[newPath + key[path.Length..]] = bytes;
            }
        }
        return Task.CompletedTask;
    }

    public void SeedDirectory(string path)
    {
        path = NormalizePath(path);
        lock (_gate) Entries[path] = null;
    }

    public void SeedFile(string path, string content)
    {
        path = NormalizePath(path);
        lock (_gate) Entries[path] = System.Text.Encoding.UTF8.GetBytes(content);
    }

    public string? ReadText(string path)
    {
        path = NormalizePath(path);
        lock (_gate) return Entries.TryGetValue(path, out var content) && content is not null
            ? System.Text.Encoding.UTF8.GetString(content) : null;
    }

    protected Dictionary<string, byte[]?> Snapshot()
    {
        lock (_gate)
            return Entries.ToDictionary(pair => pair.Key, pair => pair.Value?.ToArray(), StringComparer.Ordinal);
    }

    protected void Publish(Dictionary<string, byte[]?> entries)
    {
        lock (_gate) Entries = entries;
    }

    private sealed class WriteBackStream(Action<byte[]> publish) : MemoryStream
    {
        private bool _closed;
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                publish(ToArray());
            }
            base.Dispose(disposing);
        }
    }
}

internal sealed class InMemoryBatchFilePanelSource : MemoryFilePanelView, IFileOperationBatchProvider, ITemporaryFilePanelSource
{
    public InMemoryBatchFilePanelSource()
        : base(new PanelSourceId("memory:" + Guid.NewGuid().ToString("N"))) { }

    public bool IsTemporary => true;
    public bool FailCommit { get; set; }
    public int Begun { get; private set; }
    public int Committed { get; private set; }
    public int Aborted { get; private set; }
    public int Disposed { get; private set; }
    public FileOperationPlan? LastPlan { get; private set; }

    public ValueTask<IFileOperationBatch> BeginBatchAsync(FileOperationPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Begun++;
        LastPlan = plan;
        return ValueTask.FromResult<IFileOperationBatch>(
            new Batch(this, Snapshot()) { FailWrite = FailWrite });
    }

    private sealed class Batch(
        InMemoryBatchFilePanelSource owner, Dictionary<string, byte[]?> snapshot)
        : MemoryFilePanelView(owner.SourceId, snapshot), IFileOperationBatch
    {
        private bool _committed;
        private bool _aborted;
        private bool _disposed;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_committed) return Task.CompletedTask;
            if (_aborted) throw new InvalidOperationException("Batch was aborted.");
            if (owner.FailCommit) throw new IOException("Simulated batch commit failure.");
            owner.Publish(Snapshot());
            _committed = true;
            owner.Committed++;
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken = default)
        {
            if (!_committed && !_aborted)
            {
                _aborted = true;
                owner.Aborted++;
            }
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await AbortAsync();
            owner.Disposed++;
        }
    }
}
