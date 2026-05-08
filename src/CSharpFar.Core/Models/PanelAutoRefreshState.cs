namespace CSharpFar.Core.Models;

public sealed class PanelAutoRefreshState
{
    public bool    IsWatching             { get; init; }
    public bool    DisabledByObjectCount  { get; init; }
    public bool    DisabledForNetworkDrive { get; init; }
    public string? LastError              { get; init; }
}
