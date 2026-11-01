using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

/// <summary>Owns plugin panel sources and retires temporary sources after their last use.</summary>
public sealed class FilePanelSourceRegistry : IFilePanelSourceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<PanelSourceId, IFilePanelSource> _sources = new();
    private readonly HashSet<IFilePanelSource> _ownedSources = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PanelSourceId, int> _activeUses = new();
    private readonly Dictionary<PanelSourceId, Action<IFilePanelSource>> _pendingRetirement = new();

    public FilePanelSourceRegistry(IEnumerable<IFilePanelSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var source in sources)
            Add(source);
    }

    public void Add(IFilePanelSource source)
    {
        if (!TryAdd(source))
            throw new InvalidOperationException($"Panel source '{source.SourceId}' is already registered.");
    }

    public bool TryAdd(IFilePanelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
            return _sources.TryAdd(source.SourceId, source);
    }

    public bool TryAddOwned(IFilePanelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (!_sources.TryAdd(source.SourceId, source))
                return false;
            _ownedSources.Add(source);
            return true;
        }
    }

    public bool RemoveIfSame(PanelSourceId sourceId, IFilePanelSource source)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(sourceId, out var existing) || !ReferenceEquals(existing, source))
                return false;
            _pendingRetirement.Remove(sourceId);
            _activeUses.Remove(sourceId);
            _ownedSources.Remove(source);
            return _sources.Remove(sourceId);
        }
    }

    /// <summary>
    /// Protect temporary sources used by background file operations while their panel is closed.
    /// A lease is acquired before starting asynchronous work and released on completion.
    /// </summary>
    public IDisposable AcquireUsage(params PanelSourceId[] sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        var held = new List<PanelSourceId>();
        lock (_gate)
        {
            foreach (var id in sourceIds.Distinct())
            {
                if (!_sources.TryGetValue(id, out var source) ||
                    source is not ITemporaryFilePanelSource { IsTemporary: true })
                    continue;
                _activeUses[id] = _activeUses.GetValueOrDefault(id) + 1;
                held.Add(id);
            }
        }
        return new SourceUsage(this, held);
    }

    /// <summary>
    /// A source returned from nested navigation is displayed again. Cancel a
    /// previously scheduled retirement before releasing its navigation lease.
    /// </summary>
    public void RetainSource(PanelSourceId sourceId)
    {
        lock (_gate)
        {
            if (_sources.ContainsKey(sourceId))
                _pendingRetirement.Remove(sourceId);
        }
    }

    /// <summary>
    /// Release temporary plugin-owned sources not displayed by either panel.
    /// A source still used by a file operation is retired when its last lease ends.
    /// </summary>
    public void RemoveUnusedTemporarySources(
        PanelSourceId leftSourceId,
        PanelSourceId rightSourceId,
        Action<IFilePanelSource> dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        var released = new List<IFilePanelSource>();
        lock (_gate)
        {
            foreach (var source in _ownedSources.ToArray())
            {
                if (source is not ITemporaryFilePanelSource { IsTemporary: true })
                    continue;
                var id = source.SourceId;
                if (id == leftSourceId || id == rightSourceId)
                {
                    _pendingRetirement.Remove(id);
                    continue;
                }

                if (_activeUses.GetValueOrDefault(id) != 0)
                {
                    _pendingRetirement[id] = dispose;
                    continue;
                }

                _pendingRetirement.Remove(id);
                _sources.Remove(id);
                _ownedSources.Remove(source);
                released.Add(source);
            }
        }
        foreach (var source in released)
            dispose(source);
    }

    private void ReleaseUsage(IReadOnlyList<PanelSourceId> ids)
    {
        var released = new List<(IFilePanelSource Source, Action<IFilePanelSource> Dispose)>();
        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (!_activeUses.TryGetValue(id, out int count))
                    continue;
                if (count > 1)
                {
                    _activeUses[id] = count - 1;
                    continue;
                }
                _activeUses.Remove(id);
                if (!_pendingRetirement.Remove(id, out var dispose) ||
                    !_sources.Remove(id, out var source))
                    continue;
                _ownedSources.Remove(source);
                released.Add((source, dispose));
            }
        }
        foreach (var (source, dispose) in released)
            dispose(source);
    }

    /// <summary>Release sources at shutdown, deferring active temporary work until its last lease ends.</summary>
    public void DisposeOwnedSources()
    {
        var released = new List<IFilePanelSource>();
        lock (_gate)
        {
            foreach (var source in _ownedSources.ToArray())
            {
                var id = source.SourceId;
                if (source is ITemporaryFilePanelSource { IsTemporary: true } &&
                    _activeUses.GetValueOrDefault(id) > 0)
                {
                    _pendingRetirement[id] = DisposeSource;
                    continue;
                }
                _sources.Remove(id);
                _ownedSources.Remove(source);
                _pendingRetirement.Remove(id);
                _activeUses.Remove(id);
                released.Add(source);
            }
        }
        foreach (var source in released)
            DisposeSource(source);
    }

    private static void DisposeSource(IFilePanelSource source)
    {
        if (source is IAsyncDisposable asyncDisposable)
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else if (source is IDisposable disposable)
            disposable.Dispose();
    }

    public IFilePanelSource GetSource(PanelSourceId sourceId) =>
        TryGetSource(sourceId, out var source)
            ? source
            : throw new InvalidOperationException($"Panel source '{sourceId}' is not registered.");

    public bool TryGetSource(PanelSourceId sourceId, out IFilePanelSource source)
    {
        lock (_gate)
            return _sources.TryGetValue(sourceId, out source!);
    }

    private sealed class SourceUsage(FilePanelSourceRegistry owner, IReadOnlyList<PanelSourceId> ids) : IDisposable
    {
        private FilePanelSourceRegistry? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseUsage(ids);
    }
}
