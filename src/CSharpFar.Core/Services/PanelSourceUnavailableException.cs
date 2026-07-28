using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class PanelSourceUnavailableException : InvalidOperationException
{
    public PanelSourceUnavailableException(PanelSourceId sourceId)
        : base($"Panel source '{sourceId}' is not available in the current composition.")
    {
        SourceId = sourceId;
    }

    public PanelSourceId SourceId { get; }
}
