namespace CSharpFar.Core.Models;

public enum VolumeSelectionAction
{
    OpenVolume,
    OpenPlugin,
}

public sealed class VolumeSelectionItem
{
    public required string            Label    { get; init; }
    public string?                    Shortcut { get; init; }
    public FileSystemVolume?          Volume   { get; init; }
    public Guid?                      PluginId { get; init; }
    public Guid?                      PluginItemId { get; init; }
    public PanelSide?                 PluginPanelSide { get; init; }
    public VolumeSelectionAction      Action   { get; init; }
}
