using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IFilePanelSourceRegistry
{
    IFilePanelSource GetSource(PanelSourceId sourceId);
    bool TryGetSource(PanelSourceId sourceId, out IFilePanelSource source);
}
