namespace CSharpFar.Core.Models;

public sealed class PanelSummary
{
    public int  VisibleItemCount { get; init; }
    public int  FileCount        { get; init; }
    public int  DirectoryCount   { get; init; }
    public long TotalFileSize    { get; init; }

    public int  SelectedCount    { get; init; }
    public long SelectedFileSize { get; init; }

    public VolumeSpaceInfo? VolumeSpace           { get; init; }
    public bool             VolumeSpaceUnavailable { get; init; }
}
