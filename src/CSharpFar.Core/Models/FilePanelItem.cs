namespace CSharpFar.Core.Models;

public sealed class FilePanelItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public PanelSourceId SourceId { get; init; } = PanelSourceId.Local;
    public string SourcePath => FullPath;
    public PanelLocation Location => new(SourceId, SourcePath);
    public required bool IsDirectory { get; init; }
    public long? Size { get; init; }
    public DateTime LastWriteTime { get; init; }
    public FileAttributes Attributes { get; init; }
    public bool IsParentDirectory { get; init; }

    public bool IsVolumeMountPoint { get; init; }
    public string? MountedVolumeName { get; init; }
    public string? MountedVolumePath { get; init; }
}
