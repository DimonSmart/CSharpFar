using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class FilePanelSourceRegistry : IFilePanelSourceRegistry
{
    private readonly Dictionary<PanelSourceId, IFilePanelSource> _sources = new();

    public FilePanelSourceRegistry(IEnumerable<IFilePanelSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        foreach (var source in sources)
            Add(source);
    }

    public void Add(IFilePanelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources[source.SourceId] = source;
    }

    public IFilePanelSource GetSource(PanelSourceId sourceId) =>
        TryGetSource(sourceId, out var source)
            ? source
            : throw new InvalidOperationException($"Panel source '{sourceId}' is not registered.");

    public bool TryGetSource(PanelSourceId sourceId, out IFilePanelSource source) =>
        _sources.TryGetValue(sourceId, out source!);
}
