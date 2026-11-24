namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private bool RequestLocalRefresh(
        LocalViewerSession session,
        string filePath,
        int contentHeight,
        int width)
    {
        if (!session.PendingRefresh || _async.IsChannelPending("refresh"))
            return false;

        var reader = session.Reader;
        var state = session.State;
        long anchorByteOffset = state.TopByteOffset;
        int anchorWrappedSegment = state.TopWrappedSegmentIndex;
        LargeFileViewMode viewMode = state.ViewMode;
        int horizontalOffset = state.HorizontalOffset;
        var encodingSelection = state.EncodingSelection;
        ViewerLiveMode liveMode = state.LiveMode;
        bool wrapLines = state.WrapLines;
        bool wordWrap = state.WordWrap;
        ViewerPresentationMode presentationMode = state.PresentationMode;
        long refreshVersion = session.BeginRefresh();

        _pendingNavigation.Clear();
        _navigationRequestVersion++;
        InvalidatePreparedRender();
        long stateVersion = _async.InvalidateState();

        _async.StartLatest(
            "refresh",
            stateVersion,
            async cancellationToken =>
            {
                await Task.Yield();
                if (!LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot before) ||
                    !before.Exists)
                {
                    return null;
                }

                int failureVersion = reader.TransientFailureVersion;
                var candidateCache = new BlockCache(reader);
                LineScanner candidateScanner = await LineScanner
                    .CreateAsync(candidateCache, reader, encodingSelection, cancellationToken)
                    .ConfigureAwait(false);
                var candidateState = new LargeFileViewerState(candidateCache, candidateScanner)
                {
                    ViewMode = viewMode,
                    HorizontalOffset = horizontalOffset,
                    LiveMode = liveMode,
                    WrapLines = wrapLines,
                    WordWrap = wordWrap,
                    PresentationMode = presentationMode,
                    TopWrappedSegmentIndex = anchorWrappedSegment,
                };
                candidateState.TopByteOffset = candidateState.IsHexMode
                    ? Math.Max(0, anchorByteOffset)
                    : await candidateScanner
                        .FindLineStartAtOrBeforeAsync(anchorByteOffset, cancellationToken)
                        .ConfigureAwait(false);

                await NormalizeViewportAsync(
                        filePath,
                        reader,
                        candidateState,
                        contentHeight,
                        width,
                        keepTail: true,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (reader.TransientFailureVersion != failureVersion)
                    return null;

                if (!LocalFileMonitor.TryCaptureSnapshot(filePath, out LocalFileSnapshot after) ||
                    !after.Exists ||
                    after != before)
                {
                    return null;
                }

                return new LocalRefreshResult(
                    candidateCache,
                    candidateScanner,
                    candidateState.TopByteOffset,
                    candidateState.TopWrappedSegmentIndex,
                    after,
                    refreshVersion,
                    encodingSelection,
                    viewMode,
                    horizontalOffset);
            },
            result =>
            {
                if (result is null)
                    return;

                state.ReplaceContent(
                    result.Cache,
                    result.Scanner,
                    result.EncodingSelection);
                state.ViewMode = result.ViewMode;
                state.HorizontalOffset = result.HorizontalOffset;
                state.TopByteOffset = result.TopByteOffset;
                state.TopWrappedSegmentIndex = result.TopWrappedSegmentIndex;
                session.Commit(result.Snapshot, result.RefreshVersion);
                InvalidatePreparedRender();
            },
            ex =>
            {
                if (RandomAccessFileByteReader.IsTransientFileAccess(ex))
                    return;

                _dialogs.Message("Viewer", ex.Message);
            });

        return false;
    }

    private sealed record LocalRefreshResult(
        BlockCache Cache,
        LineScanner Scanner,
        long TopByteOffset,
        int TopWrappedSegmentIndex,
        LocalFileSnapshot Snapshot,
        long RefreshVersion,
        CSharpFar.Core.Text.TextEncodingSelection EncodingSelection,
        LargeFileViewMode ViewMode,
        int HorizontalOffset);
}
