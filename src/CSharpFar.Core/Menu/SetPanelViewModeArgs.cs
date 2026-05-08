using CSharpFar.Core.Models;

namespace CSharpFar.Core.Menu;

public sealed record SetPanelViewModeArgs
{
    public required PanelSide PanelSide { get; init; }
    public required PanelViewMode ViewMode { get; init; }
}
