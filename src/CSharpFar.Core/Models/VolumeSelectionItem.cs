namespace CSharpFar.Core.Models;

public enum VolumeSelectionAction
{
    OpenVolume,
}

public sealed class VolumeSelectionItem
{
    public required string            Label    { get; init; }
    public string?                    Shortcut { get; init; }
    public FileSystemVolume?          Volume   { get; init; }
    public VolumeSelectionAction      Action   { get; init; }
}
