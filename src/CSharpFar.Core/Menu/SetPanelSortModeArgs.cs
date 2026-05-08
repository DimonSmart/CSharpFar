using CSharpFar.Core.Models;

namespace CSharpFar.Core.Menu;

public sealed record SetPanelSortModeArgs
{
    public required PanelSide PanelSide { get; init; }
    public required SortMode SortMode { get; init; }
}
