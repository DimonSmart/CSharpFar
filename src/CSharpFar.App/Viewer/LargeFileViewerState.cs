using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

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
    public int TopWrappedSegmentIndex { get; set; }
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
    public int LastContentHeight { get; set; } = -1;

    public bool IsHexMode => ViewMode == LargeFileViewMode.Hex;

    public void ResetScanner(LineScanner lineScanner, TextEncodingSelection encodingSelection) =>
        ReplaceContent(BlockCache, lineScanner, encodingSelection);

    public void ReplaceContent(
        BlockCache blockCache,
        LineScanner lineScanner,
        TextEncodingSelection encodingSelection)
    {
        BlockCache = blockCache;
        LineScanner = lineScanner;
        EncodingSelection = encodingSelection;
        LineIndex = new SparseLineIndex();
        LineIndex.Add(1, lineScanner.ContentStartOffset);
        SearchMatch = null;
        Presentation.Reset();
    }
}
