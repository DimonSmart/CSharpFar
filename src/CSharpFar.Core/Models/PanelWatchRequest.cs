namespace CSharpFar.Core.Models;

public sealed record PanelWatchRequest
{
    public required PanelSide PanelSide { get; init; }
    public required string DirectoryPath { get; init; }
    public required int ObjectCount { get; init; }
    public required bool IsNetworkDrive { get; init; }
    public required AppSettings.PanelAutoRefreshSettings Options { get; init; }
}
