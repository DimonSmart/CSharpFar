namespace CSharpFar.App.Viewer;

internal sealed class LargeFileViewerState
{
    public LargeFileViewerState(BlockCache blockCache, LineScanner lineScanner)
    {
        BlockCache = blockCache;
        LineScanner = lineScanner;
        LineIndex.Add(1, lineScanner.ContentStartOffset);
        TopByteOffset = lineScanner.ContentStartOffset;
        ViewMode = lineScanner.IsBinary ? LargeFileViewMode.Hex : LargeFileViewMode.Text;
    }

    public long TopByteOffset { get; set; }
    public int HorizontalOffset { get; set; }
    public bool FollowMode { get; set; }
    public LargeFileViewMode ViewMode { get; set; }
    public BlockCache BlockCache { get; }
    public LineScanner LineScanner { get; }
    public SparseLineIndex LineIndex { get; } = new();

    public bool IsHexMode => ViewMode == LargeFileViewMode.Hex;
}
