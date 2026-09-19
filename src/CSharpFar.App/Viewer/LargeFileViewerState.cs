using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

internal readonly record struct ViewerSourceState(
    BlockCache BlockCache,
    LineScanner LineScanner,
    SparseLineIndex LineIndex,
    TextEncodingSelection EncodingSelection);

internal sealed class LargeFileViewerState
{
    public LargeFileViewerState(BlockCache blockCache, LineScanner lineScanner)
    {
        BlockCache = blockCache;
        LineScanner = lineScanner;
        EncodingSelection = lineScanner.Detection.Selection;
        LineIndex.Add(1, lineScanner.ContentStartOffset);
        TopByteOffset = lineScanner.ContentStartOffset;
        ViewMode = lineScanner.IsBinary ? LargeFileViewMode.Hex : LargeFileViewMode.Text;
    }

    public long TopByteOffset { get; set; }
    public int TopVisualSegment { get; set; }
    public int HorizontalOffset { get; set; }
    public ViewerLiveMode LiveMode { get; set; }
    public bool WrapLines { get; set; }
    public bool WordWrap { get; set; } = true;
    public LargeFileViewMode ViewMode { get; set; }
    public TextEncodingSelection EncodingSelection { get; private set; }
    public BlockCache BlockCache { get; private set; }
    public LineScanner LineScanner { get; private set; }
    public SparseLineIndex LineIndex { get; private set; } = new();
    public ViewerSearchRequest? LastSearch { get; set; }
    public ViewerSearchMatch? SearchMatch { get; set; }
    public ViewerPresentationMode PresentationMode { get; set; } = ViewerPresentationMode.Auto;
    public ViewerPresentationSession Presentation { get; } = new();
    public int LastViewportWidth { get; set; } = -1;
    public int LastViewportHeight { get; set; } = -1;
    public bool ViewportNeedsNormalization { get; set; } = true;

    public bool IsHexMode => ViewMode == LargeFileViewMode.Hex;

    public ViewerViewportAnchor ViewportAnchor
    {
        get => new(TopByteOffset, TopVisualSegment);
        set
        {
            TopByteOffset = value.ByteOffset;
            TopVisualSegment = Math.Max(0, value.VisualSegment);
        }
    }

    public void ResetScanner(LineScanner lineScanner, TextEncodingSelection encodingSelection)
    {
        LineScanner = lineScanner;
        EncodingSelection = encodingSelection;
        LineIndex = new SparseLineIndex();
        LineIndex.Add(1, lineScanner.ContentStartOffset);
        TopVisualSegment = 0;
        SearchMatch = null;
        Presentation.Reset();
        ViewportNeedsNormalization = true;
    }

    public ViewerSourceState CaptureSourceState() =>
        new(BlockCache, LineScanner, LineIndex, EncodingSelection);

    public void RestoreSourceState(ViewerSourceState source)
    {
        BlockCache = source.BlockCache;
        LineScanner = source.LineScanner;
        LineIndex = source.LineIndex;
        EncodingSelection = source.EncodingSelection;
        Presentation.Reset();
    }

    public void ReplaceSource(
        BlockCache blockCache,
        LineScanner lineScanner,
        TextEncodingSelection encodingSelection)
    {
        BlockCache = blockCache;
        ResetScanner(lineScanner, encodingSelection);
    }
}
