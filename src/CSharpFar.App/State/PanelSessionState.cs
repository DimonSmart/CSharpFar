using CSharpFar.Core.Models;

namespace CSharpFar.App.State;

internal sealed class PanelSessionState
{
    public required FilePanelState Left { get; set; }

    public required FilePanelState Right { get; set; }

    public PanelSide ActiveSide { get; set; } = PanelSide.Left;

    public required PanelViewMode LeftViewMode { get; set; }

    public required PanelViewMode RightViewMode { get; set; }
}
