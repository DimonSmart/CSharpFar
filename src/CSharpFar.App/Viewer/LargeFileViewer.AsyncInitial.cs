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
            cancellationToken => LineScanner.CreateAsync(
                cache,
                reader,
                cancellationToken: cancellationToken),
            scanner =>
            {
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

        // If the local async read completed synchronously (common for hot files),
        // apply it before the first surface frame and preserve the old fast path.
        _async.DrainUiCompletions();
    }

    private sealed record PendingInitialSearch(
        ViewerSearchRequest Request,
        bool SearchBackward,
        int Width);
}
