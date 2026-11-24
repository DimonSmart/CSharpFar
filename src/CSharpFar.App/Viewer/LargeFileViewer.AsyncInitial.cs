namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private PendingInitialSearch? _pendingInitialSearch;

    private void StartInitialContentLoad(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LocalViewerSession? localSession)
    {
        if (state.IsContentReady || _async.IsChannelPending("initial"))
            return;

        long stateVersion = _async.StateVersion;
        BlockCache cache = state.BlockCache;
        _async.StartLatest(
            "initial",
            stateVersion,
            async cancellationToken =>
            {
                LocalFileSnapshot? before = null;
                if (localSession is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!LocalFileMonitor.TryCaptureSnapshot(sourcePath, out LocalFileSnapshot current))
                        throw new IOException("Could not read the current file state.");
                    if (!current.Exists)
                        throw new FileNotFoundException("File not found.", sourcePath);

                    before = current;
                    localSession.Reader.UpdateKnownLength(current.Length);
                }

                LineScanner scanner = await LineScanner.CreateAsync(
                        cache,
                        reader,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (before is null || localSession is null)
                    return new InitialContentResult(scanner, null, Retry: false);

                cancellationToken.ThrowIfCancellationRequested();
                if (!LocalFileMonitor.TryCaptureSnapshot(sourcePath, out LocalFileSnapshot after))
                    throw new IOException("Could not read the current file state.");
                if (!after.Exists)
                    throw new FileNotFoundException("File not found.", sourcePath);

                return after == before.Value
                    ? new InitialContentResult(scanner, after, Retry: false)
                    : new InitialContentResult(null, after, Retry: true);
            },
            result =>
            {
                if (result.Retry)
                {
                    cache.Clear();
                    StartInitialContentLoad(sourcePath, reader, state, localSession);
                    return;
                }

                LineScanner scanner = result.Scanner
                    ?? throw new InvalidOperationException("Initial viewer load completed without a scanner.");
                if (localSession is not null && result.Snapshot is { } applied)
                    localSession.Commit(applied, refreshVersion: 0);

                state.ReplaceContent(cache, scanner, scanner.Detection.Selection);
                state.ViewMode = scanner.IsBinary
                    ? LargeFileViewMode.Hex
                    : LargeFileViewMode.Text;
                state.TopByteOffset = state.IsHexMode
                    ? 0
                    : scanner.ContentStartOffset;

                InvalidatePreparedRender();

                if (_pendingNavigation.Count > 0)
                {
                    ViewerNavigationRequest last = _pendingNavigation[^1];
                    _pendingNavigation.RemoveAt(_pendingNavigation.Count - 1);
                    QueueNavigation(
                        sourcePath,
                        reader,
                        state,
                        Math.Max(0, state.LastContentHeight),
                        Math.Max(1, state.LastViewportWidth),
                        localSession,
                        last);
                }

                if (_pendingInitialSearch is { } pendingSearch)
                {
                    _pendingInitialSearch = null;
                    StartSearch(
                        sourcePath,
                        reader,
                        state,
                        pendingSearch.Request,
                        pendingSearch.SearchBackward,
                        pendingSearch.Width);
                }
            },
            ex =>
            {
                _dialogs.Message("Viewer", ex.Message);
            });

        _async.DrainUiCompletions();
    }

    private sealed record InitialContentResult(
        LineScanner? Scanner,
        LocalFileSnapshot? Snapshot,
        bool Retry);

    private sealed record PendingInitialSearch(
        ViewerSearchRequest Request,
        bool SearchBackward,
        int Width);
}
