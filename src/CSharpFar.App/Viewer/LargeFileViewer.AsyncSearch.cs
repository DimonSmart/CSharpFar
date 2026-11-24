namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private void StartSearch(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward,
        int width)
    {
        _pendingNavigation.Clear();
        _navigationRequestVersion++;
        InvalidatePreparedRender();
        long stateVersion = _async.InvalidateState();
        LargeFileViewerState snapshot = state.CreateWorkingCopy();

        _async.StartLatest(
            "search",
            stateVersion,
            cancellationToken => FindSearchOutcomeAsync(
                filePath,
                reader,
                snapshot,
                request,
                searchBackward,
                width,
                cancellationToken),
            outcome =>
            {
                if (outcome.Match is null)
                {
                    _dialogs.Message("Find", "Text not found.");
                    return;
                }

                ViewerSearchMatch match = outcome.Match;
                state.LastSearch = request;
                state.SearchMatch = match;
                state.TopByteOffset = match.TopByteOffset;
                state.TopWrappedSegmentIndex = 0;
                if (state.LiveMode == ViewerLiveMode.Tail)
                    state.LiveMode = ViewerLiveMode.Watch;

                if (match.IsHex)
                {
                    state.ViewMode = LargeFileViewMode.Hex;
                }
                else if (!state.WrapLines && outcome.MatchCell is { } matchCell)
                {
                    int rightEdge = state.HorizontalOffset + Math.Max(1, width);
                    if (matchCell < state.HorizontalOffset || matchCell >= rightEdge)
                        state.HorizontalOffset = Math.Max(0, matchCell - 4);
                }

                InvalidatePreparedRender();
            },
            ex =>
            {
                if (ex is ArgumentException)
                    _dialogs.Message("Find", ex.Message);
                else
                    _dialogs.Message("Viewer", ex.Message);
            });
    }

    private async Task<ViewerSearchOutcome> FindSearchOutcomeAsync(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        ViewerSearchRequest request,
        bool searchBackward,
        int width,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        ViewerSearchMatch? match = await ViewerSearchEngine
            .FindAsync(reader, state, request, searchBackward, cancellationToken)
            .ConfigureAwait(false);
        if (match is null || match.IsHex || state.WrapLines)
            return new ViewerSearchOutcome(match, null);

        int captureCharacters = Math.Min(
            1_000_000,
            match.CharacterIndex + match.CharacterLength + 32);
        ScannedLines scanned = await state.LineScanner
            .ReadLinesAsync(
                match.LineStartOffset,
                1,
                Math.Max(256, captureCharacters * 4),
                cancellationToken)
            .ConfigureAwait(false);
        if (scanned.Lines.Count == 0)
            return new ViewerSearchOutcome(match, match.CharacterIndex);

        var presentation = new ViewerPresentationSession();
        IReadOnlyList<PresentedLine> presented = await presentation
            .PresentAsync(
                state.PresentationMode,
                filePath,
                state.LineScanner,
                scanned.Lines,
                width,
                cancellationToken)
            .ConfigureAwait(false);

        PresentedLine line = ResolvePresentationForSearch(presented[0], match);
        int matchCell = match.CharacterIndex;
        if (line.TryMapSourceRange(
                match.CharacterIndex,
                match.CharacterLength,
                out int presentedMatchStart,
                out _))
        {
            matchCell = new ViewerTextLayout(line.Text)
                .CellOffsetFromSourceIndex(presentedMatchStart);
        }

        return new ViewerSearchOutcome(match, matchCell);
    }

    private sealed record ViewerSearchOutcome(
        ViewerSearchMatch? Match,
        int? MatchCell);
}
