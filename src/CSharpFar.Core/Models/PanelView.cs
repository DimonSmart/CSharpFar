namespace CSharpFar.Core.Models;

public sealed class PanelView
{
    public required IReadOnlyList<FilePanelItem> Items            { get; init; }
    public required PanelSummary                 Summary          { get; init; }
    public required PanelAutoRefreshState        AutoRefreshState { get; init; }
    public required bool                         IsRootDirectory  { get; init; }
}
