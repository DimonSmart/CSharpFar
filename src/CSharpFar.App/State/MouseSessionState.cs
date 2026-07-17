using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class MouseSessionState
{
    public PanelItemClick? LastLeftPanelItemClick { get; set; }
}

internal readonly record struct PanelItemClick(
    PanelSide Side,
    int Index,
    PanelLocation Location);
