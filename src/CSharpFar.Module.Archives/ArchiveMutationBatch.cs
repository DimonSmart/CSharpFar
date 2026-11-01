using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Module.Archives;

internal sealed record ArchiveMutationEntry(
    string Path, bool IsDirectory, long Size, DateTime Modified,
    string? OriginalPath, string? StagingFile, bool Explicit);

/// <summary>
/// Private copy-on-write archive view. Files are staged on disk, never in RAM;
/// changes to the source file only occur in ArchivePublication.PublishAsync.
/// </summary>
internal sealed class ArchiveMutationBatch : IFileOperationBatch
{
    private readonly ArchiveFilePanelSource _owner;
    private readonly Dictionary<string, ArchiveMutationEntry> _entries;
    private readonly Dictionary<string, ArchiveNode> _originalNodes;
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly byte[] _originalDigest;
    private int _activeWriters;
    private bool _failed;
    private bool _committed;
    private bool _aborted;
    private bool _disposed;

    internal ArchiveMutationBatch(ArchiveFilePanelSource owner, byte[] originalDigest)
    {
        _owner = owner;
        _originalDigest = originalDigest;
        _originalNodes = owner.Index.Nodes.ToDictionary(node => node.Path, StringComparer.Ordinal);
        _entries = owner.Index.Nodes.Where(node => node.Path != "/").ToDictionary(node => node.Path,
            node => new ArchiveMutationEntry(node.Path, node.IsDirectory, node.Size,
                node.Modified, node.Path, null, node.Explicit),
            StringComparer.Ordinal);
    }

    internal IReadOnlyCollection<ArchiveMutationEntry> Entries => _entries.Values;
    internal IReadOnlyDictionary<string, ArchiveNode> OriginalNodes => _originalNodes;
    internal byte[] OriginalDigest => _originalDigest;

    public PanelSourceId SourceId => _owner.SourceId;
    public string DisplayName => _owner.DisplayName;
    public PanelProviderCapabilities Capabilities => _owner.Capabilities;
    public IReadOnlyCollection<char> PathSeparators => _owner.PathSeparators;
    public string NormalizePath(string path) => _owner.NormalizePath(path);
    public bool IsRootPath(string path) => _owner.IsRootPath(path);
    public string? GetParentPath(string path) => _owner.GetParentPath(path);

    private void CheckWorking()
    {
        if (_disposed || _committed || _aborted)
            throw new InvalidOperationException("Archive mutation batch has finished.");
        if (_failed)
            throw new IOException("A staged write failed. Abort the batch.");
    }

    private static bool ChildOf(string path, string parent) =>
        path.StartsWith(parent + "/", StringComparison.Ordinal);

    private static FilePanelItem Item(ArchiveMutationEntry entry, PanelSourceId id) => new()
    {
        Name = entry.Path[(entry.Path.LastIndexOf('/') + 1)..],
        FullPath = entry.Path,
        SourceId = id,
        IsDirectory = entry.IsDirectory,
        Size = entry.IsDirectory ? null : entry.Size,
        LastWriteTime = entry.Modified,
        Attributes = entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
    };

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string path = NormalizePath(sourcePath);
        if (path != "/" && (!_entries.TryGetValue(path, out var item) || !item.IsDirectory))
            throw new DirectoryNotFoundException(path);
        return _entries.Values.Where(entry => ArchivePath.Parent(entry.Path) == path)
            .Select(entry => Item(entry, SourceId)).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
    }

    public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        return _entries.TryGetValue(NormalizePath(sourcePath), out var item)
            ? Item(item, SourceId) : null;
    }

    public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string path = NormalizePath(sourcePath);
        if (!_entries.TryGetValue(path, out var entry) || entry.IsDirectory)
            throw new FileNotFoundException("Archive entry not found.", path);
        if (entry.StagingFile is not null)
            return Task.FromResult<Stream>(new FileStream(entry.StagingFile, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous));
        if (entry.OriginalPath is null || !_originalNodes.TryGetValue(entry.OriginalPath, out var original))
            throw new IOException("Archive entry is missing its source.");
        return _owner.OpenOriginalEntryAsync(original, cancellationToken);
    }

    public Task<Stream> OpenWriteAsync(
        string sourcePath, bool overwrite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string path = NormalizePath(sourcePath);
        if (path == "/")
            throw new IOException("Cannot write to archive root.");
        EnsureParent(path);
        _entries.TryGetValue(path, out var old);
        if (old is not null)
        {
            if (old.IsDirectory)
                throw new IOException("Cannot overwrite a directory with a file.");
            if (!overwrite)
                throw new IOException("Archive entry already exists.");
        }

        string staged = Path.Combine(Path.GetTempPath(),
            "csharpfar-stage-" + Guid.NewGuid().ToString("N") + ".tmp");
        var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _temporaryFiles.Add(staged);
        lock (_gate) _activeWriters++;
        return Task.FromResult<Stream>(new StagedWriteStream(stream,
            () =>
            {
                if (_aborted)
                {
                    try { File.Delete(staged); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    return;
                }
                long size = new FileInfo(staged).Length;
                if (size > ArchiveEntryIndex.MaxSingleSize)
                    throw new InvalidDataException("Archive entry exceeds the single-file size limit.");
                _entries[path] = new ArchiveMutationEntry(path, false, size, DateTime.UtcNow,
                    old?.OriginalPath, staged, true);
            },
            () => { lock (_gate) { _failed = true; } },
            () => { lock (_gate) { _activeWriters--; } }));
    }

    public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string path = NormalizePath(sourcePath);
        if (path == "/") return Task.CompletedTask;
        EnsureParent(path);
        if (_entries.TryGetValue(path, out var current))
        {
            if (!current.IsDirectory)
                throw new IOException("File already exists at directory path.");
            return Task.CompletedTask;
        }
        _entries.Add(path, new ArchiveMutationEntry(path, true, 0, DateTime.UtcNow,
            null, null, true));
        return Task.CompletedTask;
    }

    private void EnsureParent(string path)
    {
        string? parent = ArchivePath.Parent(path);
        if (parent is null || parent == "/") return;
        if (!_entries.TryGetValue(parent, out var node) || !node.IsDirectory)
            throw new DirectoryNotFoundException("Archive parent directory does not exist: " + parent);
    }

    public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string path = NormalizePath(sourcePath);
        if (path == "/")
            throw new IOException("Cannot delete archive root.");
        if (!_entries.ContainsKey(path))
            throw new FileNotFoundException("Archive entry not found.", path);
        var targets = _entries.Keys.Where(key => key == path || ChildOf(key, path)).ToArray();
        if (!recursive && targets.Length > 1)
            throw new IOException("Directory is not empty.");
        foreach (var key in targets)
            _entries.Remove(key);
        return Task.CompletedTask;
    }

    public Task RenameAsync(
        string sourcePath, string newSourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckWorking();
        string from = NormalizePath(sourcePath);
        string to = NormalizePath(newSourcePath);
        if (from == "/" || to == "/")
            throw new IOException("Cannot rename archive root.");
        if (!_entries.ContainsKey(from))
            throw new FileNotFoundException("Archive source entry not found.", from);
        if (from == to) return Task.CompletedTask;
        if (ChildOf(to, from))
            throw new IOException("Cannot move a directory into itself.");
        if (_entries.ContainsKey(to))
            throw new IOException("Destination archive entry already exists.");
        EnsureParent(to);

        var affected = _entries.Values.Where(entry => entry.Path == from || ChildOf(entry.Path, from)).ToArray();
        foreach (var entry in affected)
        {
            string renamed = to + entry.Path[from.Length..];
            if (_entries.ContainsKey(renamed) && !affected.Contains(_entries[renamed]))
                throw new IOException("Archive path collision: " + renamed);
        }
        foreach (var entry in affected)
            _entries.Remove(entry.Path);
        foreach (var entry in affected)
        {
            string renamed = to + entry.Path[from.Length..];
            _entries.Add(renamed, entry with { Path = renamed, Explicit = true });
        }
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_committed) return;
        CheckWorking();
        lock (_gate)
        {
            if (_activeWriters != 0)
                throw new IOException("Cannot commit while archive write streams are still open.");
        }
        long size = 0;
        foreach (var entry in _entries.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsDirectory)
                size = checked(size + entry.Size);
        }
        if (_entries.Count > ArchiveEntryIndex.MaxEntries * 2 ||
            size > ArchiveEntryIndex.MaxTotalSize)
            throw new InvalidDataException("Archive staging exceeds the safety limits.");
        await ArchivePublication.PublishAsync(_owner, this, cancellationToken).ConfigureAwait(false);
        _committed = true;
        Cleanup();
    }

    public Task AbortAsync(CancellationToken cancellationToken = default)
    {
        if (!_committed && !_aborted)
        {
            _aborted = true;
            Cleanup();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await AbortAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void Cleanup()
    {
        foreach (var path in _temporaryFiles)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _temporaryFiles.Clear();
    }

    private sealed class StagedWriteStream(
        Stream inner, Action completed, Action failed, Action released) : Stream
    {
        private bool _closed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_closed;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush()
        {
            try { inner.Flush(); } catch { failed(); throw; }
        }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            try { await inner.FlushAsync(cancellationToken).ConfigureAwait(false); }
            catch { failed(); throw; }
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            try { inner.Write(buffer, offset, count); } catch { failed(); throw; }
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try { inner.Write(buffer); } catch { failed(); throw; }
        }
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false); }
            catch { failed(); throw; }
        }
        public override async Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try { await inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false); }
            catch { failed(); throw; }
        }
        private void Finish()
        {
            if (_closed) return;
            _closed = true;
            try { inner.Dispose(); completed(); }
            catch { failed(); throw; }
            finally { released(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Finish();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (_closed) return;
            _closed = true;
            try { await inner.DisposeAsync().ConfigureAwait(false); completed(); }
            catch { failed(); throw; }
            finally { released(); }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
