using CSharpFar.Core.Models;

namespace CSharpFar.Core.Menu;

public sealed record PanelCommandArgs
{
    public required PanelSide PanelSide { get; init; }
}
