using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class FilePanelSourceRegistry : IFilePanelSourceRegistry
{
    private readonly Dictionary<PanelSourceId, IFilePanelSource> _sources = new();
    private readonly HashSet<IFilePanelSource> _ownedSources = new(ReferenceEqualityComparer.Instance);

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
        return _sources.TryAdd(source.SourceId, source);
    }

    public bool TryAddOwned(IFilePanelSource source)
    {
        if (!TryAdd(source)) return false;
        _ownedSources.Add(source);
        return true;
    }

    public bool RemoveIfSame(PanelSourceId sourceId, IFilePanelSource source)
    {
        if (!_sources.TryGetValue(sourceId, out var existing) || !ReferenceEquals(existing, source))
            return false;
        _ownedSources.Remove(source);
        return _sources.Remove(sourceId);
    }

    /// <summary>Release plugin-owned sources when the application session terminates.</summary>
    public void DisposeOwnedSources()
    {
        var sources = _ownedSources.ToArray();
        _ownedSources.Clear();
        foreach (var source in sources)
            _sources.Remove(source.SourceId);
        foreach (var source in sources)
        {
            if (source is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else if (source is IDisposable disposable)
                disposable.Dispose();
        }
    }

    public IFilePanelSource GetSource(PanelSourceId sourceId) =>
        TryGetSource(sourceId, out var source)
            ? source
            : throw new InvalidOperationException($"Panel source '{sourceId}' is not registered.");

    public bool TryGetSource(PanelSourceId sourceId, out IFilePanelSource source) =>
        _sources.TryGetValue(sourceId, out source!);
}
