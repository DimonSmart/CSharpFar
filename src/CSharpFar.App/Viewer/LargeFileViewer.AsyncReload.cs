namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private void RequestScannerReload(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        long anchorByteOffset = state.TopByteOffset;
        int anchorWrappedSegment = state.TopWrappedSegmentIndex;
        LargeFileViewMode viewMode = state.ViewMode;
        int horizontalOffset = state.HorizontalOffset;
        var encodingSelection = state.EncodingSelection;
        ViewerLiveMode liveMode = state.LiveMode;
        bool wrapLines = state.WrapLines;
        bool wordWrap = state.WordWrap;
        ViewerPresentationMode presentationMode = state.PresentationMode;

        _pendingNavigation.Clear();
        _navigationRequestVersion++;
        InvalidatePreparedRender();
        long stateVersion = _async.InvalidateState();

        _async.StartLatest(
            "reload",
            stateVersion,
            async cancellationToken =>
            {
                await Task.Yield();
                var cache = new BlockCache(reader);
                LineScanner scanner = await LineScanner
                    .CreateAsync(cache, reader, encodingSelection, cancellationToken)
                    .ConfigureAwait(false);
                var candidate = new LargeFileViewerState(cache, scanner)
                {
                    ViewMode = viewMode,
                    HorizontalOffset = horizontalOffset,
                    LiveMode = liveMode,
                    WrapLines = wrapLines,
                    WordWrap = wordWrap,
                    PresentationMode = presentationMode,
                    TopWrappedSegmentIndex = anchorWrappedSegment,
                };
                candidate.TopByteOffset = candidate.IsHexMode
                    ? Math.Clamp(anchorByteOffset, 0, reader.Length)
                    : await scanner
                        .FindLineStartAtOrBeforeAsync(anchorByteOffset, cancellationToken)
                        .ConfigureAwait(false);

                await NormalizeViewportAsync(
                        sourcePath,
                        reader,
                        candidate,
                        contentHeight,
                        width,
                        keepTail: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                return new ScannerReloadResult(
                    cache,
                    scanner,
                    candidate.TopByteOffset,
                    candidate.TopWrappedSegmentIndex,
                    encodingSelection,
                    viewMode,
                    horizontalOffset);
            },
            result =>
            {
                state.ReplaceContent(
                    result.Cache,
                    result.Scanner,
                    result.EncodingSelection);
                state.ViewMode = result.ViewMode;
                state.HorizontalOffset = result.HorizontalOffset;
                state.TopByteOffset = result.TopByteOffset;
                state.TopWrappedSegmentIndex = result.TopWrappedSegmentIndex;
                InvalidatePreparedRender();
            },
            ex => _dialogs.Message("Viewer", ex.Message));
    }

    private sealed record ScannerReloadResult(
        BlockCache Cache,
        LineScanner Scanner,
        long TopByteOffset,
        int TopWrappedSegmentIndex,
        CSharpFar.Core.Text.TextEncodingSelection EncodingSelection,
        LargeFileViewMode ViewMode,
        int HorizontalOffset);
}
