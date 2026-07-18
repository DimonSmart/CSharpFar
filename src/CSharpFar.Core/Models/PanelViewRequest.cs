namespace CSharpFar.Core.Models;

public sealed class PanelViewRequest
{
    public required string DirectoryPath { get; init; }
    public PanelLocation? Location { get; init; }
    public required AppSettings.PanelOptionsSettings Options { get; init; }
    public required SortMode SortMode { get; init; }
    public required bool SortDescending { get; init; }
    public required IReadOnlySet<string> SelectedPaths { get; init; }
}
