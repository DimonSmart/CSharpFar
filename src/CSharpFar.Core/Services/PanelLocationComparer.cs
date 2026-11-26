using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

/// <summary>Compares logical locations using a fixed snapshot of source semantics.</summary>
public sealed class PanelLocationComparer : IEqualityComparer<PanelLocation>
{
    private readonly Dictionary<PanelSourceId, (IFilePanelSource Source, StringComparer Comparer)> _sources;

    public PanelLocationComparer(IEnumerable<IFilePanelSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToDictionary(source => source.SourceId, source => (source, source.PathComparer));
    }

    public bool Equals(PanelLocation x, PanelLocation y)
    {
        if (x.SourceId != y.SourceId)
            return false;
        if (!_sources.TryGetValue(x.SourceId, out var semantics))
            return StringComparer.Ordinal.Equals(x.SourcePath, y.SourcePath);

        return semantics.Comparer.Equals(
            semantics.Source.NormalizePath(x.SourcePath),
            semantics.Source.NormalizePath(y.SourcePath));
    }

    public int GetHashCode(PanelLocation obj)
    {
        int pathHash = _sources.TryGetValue(obj.SourceId, out var semantics)
            ? semantics.Comparer.GetHashCode(semantics.Source.NormalizePath(obj.SourcePath))
            : StringComparer.Ordinal.GetHashCode(obj.SourcePath);
        return HashCode.Combine(obj.SourceId, pathHash);
    }
}
