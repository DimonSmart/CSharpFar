namespace CSharpFar.Core.Models;

public enum VolumeSelectionAction
{
    OpenVolume,
    OpenModule,
}

public sealed class VolumeSelectionItem
{
    public required string Label { get; init; }
    public string? Shortcut { get; init; }
    public FileSystemVolume? Volume { get; init; }
    public Guid? ModuleActionId { get; init; }
    public PanelSide? ModulePanelSide { get; init; }
    public VolumeSelectionAction Action { get; init; }
}
