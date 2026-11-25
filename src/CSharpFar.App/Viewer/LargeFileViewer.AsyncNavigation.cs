namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private readonly List<ViewerNavigationRequest> _pendingNavigation = [];
    private long _navigationRequestVersion;

    private void QueueNavigation(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        LocalViewerSession? localSession,
        ViewerNavigationRequest request)
    {
        _pendingNavigation.Add(request);
        long requestVersion = ++_navigationRequestVersion;
        InvalidatePreparedRender();
        if (!state.IsContentReady)
            return;

        ViewerNavigationRequest[] requests = _pendingNavigation.ToArray();
        bool hasLocalSession = localSession is not null;

        long stateVersion = _async.InvalidateState();

        LargeFileViewerState fastWorking = state.CreateWorkingCopy();
        using (fastWorking.BlockCache.EnterCacheOnly())
        {
            Task<LargeFileViewerState> fast = ApplyNavigationRequestsAsync(
                sourcePath,
                reader,
                fastWorking,
                contentHeight,
                width,
                hasLocalSession,
                requests,
                CancellationToken.None);
            if (fast.IsCompletedSuccessfully)
            {
                ApplyNavigationResult(state, localSession, fast.Result, requestVersion);
                return;
            }

            _ = fast.Exception;
        }

        LargeFileViewerState working = state.CreateWorkingCopy();
        _async.StartLatest(
            "navigation",
            stateVersion,
            async cancellationToken =>
            {
                await Task.Yield();
                return await ApplyNavigationRequestsAsync(
                        sourcePath,
                        reader,
                        working,
                        contentHeight,
                        width,
                        hasLocalSession,
                        requests,
                        cancellationToken)
                    .ConfigureAwait(false);
            },
            result => ApplyNavigationResult(state, localSession, result, requestVersion),
            ex =>
            {
                if (requestVersion != _navigationRequestVersion)
                    return;

                _pendingNavigation.Clear();
                _dialogs.Message("Viewer", ex.Message);
            });
    }

    private async Task<LargeFileViewerState> ApplyNavigationRequestsAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState working,
        int contentHeight,
        int width,
        bool hasLocalSession,
        IReadOnlyList<ViewerNavigationRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (ViewerNavigationRequest pending in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyNavigationRequestAsync(
                    sourcePath,
                    reader,
                    working,
                    contentHeight,
                    width,
                    hasLocalSession,
                    pending,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return working;
    }

    private void ApplyNavigationResult(
        LargeFileViewerState state,
        LocalViewerSession? localSession,
        LargeFileViewerState result,
        long requestVersion)
    {
        if (requestVersion != _navigationRequestVersion)
            return;

        ViewerLiveMode oldLiveMode = state.LiveMode;
        ViewerLiveMode desiredLiveMode = result.LiveMode;
        state.ApplyWorkingCopy(result);
        state.LiveMode = oldLiveMode;
        SetLiveMode(localSession, state, desiredLiveMode);
        _pendingNavigation.Clear();
        InvalidatePreparedRender();
    }

    private void CancelPendingNavigation()
    {
        if (_pendingNavigation.Count == 0)
            return;

        _pendingNavigation.Clear();
        _navigationRequestVersion++;
        InvalidatePreparedRender();
        _async.InvalidateState();
    }

    private async Task ApplyNavigationRequestAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        bool hasLocalSession,
        ViewerNavigationRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Kind)
        {
            case ViewerNavigationKind.Up:
                await MoveUpAsync(sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.Down:
                await MoveDownAsync(sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterDownwardNavigationAsync(
                        hasLocalSession, sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.PageUp:
            case ViewerNavigationKind.FastPageUp:
                await MovePageUpAsync(
                        sourcePath,
                        reader,
                        state,
                        contentHeight,
                        width,
                        request.Kind == ViewerNavigationKind.FastPageUp ? FastPageMultiplier : 1,
                        cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.PageDown:
            case ViewerNavigationKind.FastPageDown:
                await MovePageDownAsync(
                        sourcePath,
                        reader,
                        state,
                        contentHeight,
                        width,
                        request.Kind == ViewerNavigationKind.FastPageDown ? FastPageMultiplier : 1,
                        cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterDownwardNavigationAsync(
                        hasLocalSession, sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.Home:
                state.TopByteOffset = state.IsHexMode ? 0 : state.LineScanner.ContentStartOffset;
                state.TopWrappedSegmentIndex = 0;
                state.HorizontalOffset = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.End:
            {
                var localReader = reader as ILocalFileByteReader;
                int failureVersion = localReader?.TransientFailureVersion ?? 0;
                ViewerViewportPosition maximum = await GetMaximumViewportPositionAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                if (localReader is not null &&
                    localReader.TransientFailureVersion != failureVersion)
                {
                    break;
                }

                ApplyViewportPosition(state, maximum);
                state.HorizontalOffset = 0;
                if (hasLocalSession)
                    state.LiveMode = ViewerLiveMode.Tail;
                break;
            }

            case ViewerNavigationKind.ToggleWrap:
                state.WrapLines = !state.WrapLines;
                state.TopWrappedSegmentIndex = 0;
                if (state.WrapLines)
                    state.HorizontalOffset = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: true, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.ToggleWordWrap:
                state.WordWrap = !state.WordWrap;
                state.WrapLines = true;
                state.HorizontalOffset = 0;
                state.TopWrappedSegmentIndex = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: true, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.ToggleViewMode:
                if (state.IsHexMode)
                {
                    state.ViewMode = LargeFileViewMode.Text;
                    state.TopByteOffset = await state.LineScanner
                        .FindLineStartAtOrBeforeAsync(state.TopByteOffset, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    state.ViewMode = LargeFileViewMode.Hex;
                    state.TopByteOffset = 0;
                }

                state.TopWrappedSegmentIndex = 0;
                state.HorizontalOffset = 0;
                state.SearchMatch = null;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: true, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.TogglePresentation:
                state.PresentationMode = state.PresentationMode == ViewerPresentationMode.Auto
                    ? ViewerPresentationMode.Raw
                    : ViewerPresentationMode.Auto;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: true, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.JumpPercent:
            {
                long target = reader.Length * request.Value / 100;
                state.TopByteOffset = state.IsHexMode
                    ? Math.Clamp(target, 0, reader.Length)
                    : await state.LineScanner
                        .FindLineStartAtOrBeforeAsync(target, cancellationToken)
                        .ConfigureAwait(false);
                state.TopWrappedSegmentIndex = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;
            }

            case ViewerNavigationKind.JumpByte:
                state.TopByteOffset = Math.Clamp(request.Value, 0, reader.Length);
                state.TopWrappedSegmentIndex = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ViewerNavigationKind.JumpLine:
                state.TopByteOffset = await state.LineScanner
                    .FindLineOffsetAsync(request.Value, state.LineIndex, cancellationToken)
                    .ConfigureAwait(false);
                state.TopWrappedSegmentIndex = 0;
                await NormalizeViewportAsync(
                        sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
                    .ConfigureAwait(false);
                await UpdateLiveModeAfterAwayNavigationAsync(
                        sourcePath, reader, state, contentHeight, width, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"Unknown viewer navigation request '{request.Kind}'.");
        }
    }

    private async Task MoveUpAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(0, state.TopByteOffset - BinaryBytesPerRow);
        }
        else if (state.WrapLines)
        {
            await MoveWrappedUpOneAsync(sourcePath, state, width, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            state.TopByteOffset = await state.LineScanner
                .FindPreviousLineStartAsync(state.TopByteOffset, cancellationToken)
                .ConfigureAwait(false);
        }

        await NormalizeViewportAsync(
                sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MoveDownAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset += BinaryBytesPerRow;
        }
        else if (state.WrapLines)
        {
            await MoveWrappedDownOneAsync(sourcePath, reader, state, width, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            ScannedLines scanned = await state.LineScanner
                .ReadLinesAsync(state.TopByteOffset, 2, 256, cancellationToken)
                .ConfigureAwait(false);
            state.TopByteOffset = scanned.Lines.Count > 1
                ? scanned.Lines[1].StartOffset
                : scanned.NextOffset;
        }

        await NormalizeViewportAsync(
                sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MovePageUpAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages,
        CancellationToken cancellationToken)
    {
        int steps = Math.Max(1, contentHeight) * Math.Max(1, pages);
        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(
                0,
                state.TopByteOffset - (long)steps * BinaryBytesPerRow);
        }
        else if (state.WrapLines)
        {
            for (int i = 0; i < steps; i++)
            {
                if (!await MoveWrappedUpOneAsync(sourcePath, state, width, cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        else
        {
            for (int i = 0; i < steps; i++)
            {
                long previous = await state.LineScanner
                    .FindPreviousLineStartAsync(state.TopByteOffset, cancellationToken)
                    .ConfigureAwait(false);
                if (previous == state.TopByteOffset)
                    break;
                state.TopByteOffset = previous;
            }
        }

        await NormalizeViewportAsync(
                sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MovePageDownAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages,
        CancellationToken cancellationToken)
    {
        int steps = Math.Max(1, contentHeight) * Math.Max(1, pages);
        if (state.IsHexMode)
        {
            state.TopByteOffset += (long)steps * BinaryBytesPerRow;
        }
        else if (state.WrapLines)
        {
            for (int i = 0; i < steps; i++)
            {
                if (!await MoveWrappedDownOneAsync(sourcePath, reader, state, width, cancellationToken)
                        .ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        else
        {
            for (int page = 0; page < Math.Max(1, pages); page++)
            {
                ScannedLines scanned = await state.LineScanner
                    .ReadLinesAsync(
                        state.TopByteOffset,
                        Math.Max(1, contentHeight),
                        256,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (scanned.NextOffset <= state.TopByteOffset)
                    break;
                state.TopByteOffset = scanned.NextOffset;
            }
        }

        await NormalizeViewportAsync(
                sourcePath, reader, state, contentHeight, width, keepTail: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> MoveWrappedUpOneAsync(
        string sourcePath,
        LargeFileViewerState state,
        int width,
        CancellationToken cancellationToken)
    {
        if (state.TopWrappedSegmentIndex > 0)
        {
            state.TopWrappedSegmentIndex--;
            return true;
        }

        long previous = await state.LineScanner
            .FindPreviousLineStartAsync(state.TopByteOffset, cancellationToken)
            .ConfigureAwait(false);
        if (previous == state.TopByteOffset)
            return false;

        state.TopByteOffset = previous;
        state.TopWrappedSegmentIndex = Math.Max(
            0,
            await GetWrappedSegmentCountAsync(
                    sourcePath, state, previous, width, cancellationToken)
                .ConfigureAwait(false) - 1);
        return true;
    }

    private async Task<bool> MoveWrappedDownOneAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int width,
        CancellationToken cancellationToken)
    {
        ScannedLines scanned = await state.LineScanner
            .ReadLinesAsync(
                state.TopByteOffset,
                1,
                MaxWrappedLineCaptureBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (scanned.Lines.Count == 0)
            return false;

        ScannedLine line = scanned.Lines[0];
        int segmentCount = await GetWrappedSegmentCountAsync(
                sourcePath, state, line, width, cancellationToken)
            .ConfigureAwait(false);
        if (state.TopWrappedSegmentIndex + 1 < segmentCount)
        {
            state.TopWrappedSegmentIndex++;
            return true;
        }

        if (line.NextOffset <= state.TopByteOffset || line.NextOffset > reader.Length)
            return false;

        state.TopByteOffset = line.NextOffset;
        state.TopWrappedSegmentIndex = 0;
        return true;
    }

    private async Task<ViewerViewportPosition> GetMaximumViewportPositionAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        EnsureLayoutCacheContext(state);
        var localReader = reader as ILocalFileByteReader;
        int failureVersion = localReader?.TransientFailureVersion ?? 0;
        long length = reader.Length;
        MaximumViewportCacheKey key = CreateMaximumViewportCacheKey(
            sourcePath,
            state,
            length,
            contentHeight,
            width);

        if (_maximumViewportCache is { } cached && cached.Key == key)
            return cached.Position;

        ViewerViewportPosition result = await ComputeMaximumViewportPositionAsync(
                sourcePath,
                state,
                length,
                contentHeight,
                width,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (localReader is null || localReader.TransientFailureVersion == failureVersion)
            _maximumViewportCache = new MaximumViewportCacheEntry(key, result);

        return result;
    }

    private async Task<ViewerViewportPosition> ComputeMaximumViewportPositionAsync(
        string sourcePath,
        LargeFileViewerState state,
        long length,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        int visibleRows = Math.Max(1, contentHeight);

        if (state.IsHexMode)
        {
            long rowCount = (length + BinaryBytesPerRow - 1) / BinaryBytesPerRow;
            long maxTopRow = Math.Max(0, rowCount - visibleRows);
            return new ViewerViewportPosition(maxTopRow * BinaryBytesPerRow, 0);
        }

        if (length <= state.LineScanner.ContentStartOffset)
            return new ViewerViewportPosition(state.LineScanner.ContentStartOffset, 0);

        if (!state.WrapLines)
        {
            long offset = await state.LineScanner
                .FindTailTopOffsetAsync(visibleRows, cancellationToken)
                .ConfigureAwait(false);
            return new ViewerViewportPosition(offset, 0);
        }

        long lineStart = await state.LineScanner
            .FindTailTopOffsetAsync(1, cancellationToken)
            .ConfigureAwait(false);
        int remainingRows = visibleRows;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int segmentCount = await GetWrappedSegmentCountAsync(
                    sourcePath, state, lineStart, width, cancellationToken)
                .ConfigureAwait(false);
            if (segmentCount >= remainingRows)
            {
                return new ViewerViewportPosition(
                    lineStart,
                    Math.Max(0, segmentCount - remainingRows));
            }

            remainingRows -= segmentCount;
            long previous = await state.LineScanner
                .FindPreviousLineStartAsync(lineStart, cancellationToken)
                .ConfigureAwait(false);
            if (previous == lineStart)
                return new ViewerViewportPosition(lineStart, 0);

            lineStart = previous;
        }
    }

    private async Task<int> GetWrappedSegmentCountAsync(
        string sourcePath,
        LargeFileViewerState state,
        long lineStart,
        int width,
        CancellationToken cancellationToken)
    {
        ScannedLines scanned = await state.LineScanner
            .ReadLinesAsync(lineStart, 1, MaxWrappedLineCaptureBytes, cancellationToken)
            .ConfigureAwait(false);
        if (scanned.Lines.Count == 0)
            return 1;

        return await GetWrappedSegmentCountAsync(
                sourcePath, state, scanned.Lines[0], width, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<int> GetWrappedSegmentCountAsync(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width,
        CancellationToken cancellationToken)
    {
        if (RequiresStreamingWrappedLayout(line))
        {
            var builder = new StreamingWrappedLineBuilder(
                Math.Max(1, width),
                state.WordWrap,
                captureStartSegment: 0,
                captureSegmentCount: 0);
            await state.LineScanner
                .VisitLineTextChunksAsync(line, builder.Append, cancellationToken)
                .ConfigureAwait(false);
            return builder.Complete().SegmentCount;
        }

        WrappedLineLayout layout = await GetOrCreateWrappedLineLayoutAsync(
                sourcePath,
                state,
                line,
                width,
                cancellationToken)
            .ConfigureAwait(false);
        return layout.Segments.Count;
    }

    private async Task<WrappedLineLayout> GetOrCreateWrappedLineLayoutAsync(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width,
        CancellationToken cancellationToken)
    {
        if (TryGetWrappedLineLayout(sourcePath, state, line, width, out WrappedLineLayout cached))
            return cached;

        IReadOnlyList<PresentedLine> presented = await state.Presentation
            .PresentAsync(
                state.PresentationMode,
                sourcePath,
                state.LineScanner,
                [line],
                Math.Max(1, width),
                cancellationToken)
            .ConfigureAwait(false);
        PresentedLine resolved = ResolvePresentationForSearch(presented[0], state.SearchMatch);

        if (resolved.PreserveLineLayout)
            ClearWrappedLayoutCache();

        WrappedLineLayout layout = CreateWrappedLineLayout(resolved, state.WordWrap, width);
        StoreWrappedLineLayout(
            CreateWrappedLayoutCacheKey(sourcePath, state, line, width),
            layout);
        return layout;
    }

    private async Task NormalizeViewportAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        bool keepTail,
        CancellationToken cancellationToken)
    {
        var localReader = reader as ILocalFileByteReader;
        int failureVersion = localReader?.TransientFailureVersion ?? 0;
        var originalPosition = new ViewerViewportPosition(
            state.TopByteOffset,
            state.TopWrappedSegmentIndex);

        ViewerViewportPosition maximum = await GetMaximumViewportPositionAsync(
                sourcePath, reader, state, contentHeight, width, cancellationToken)
            .ConfigureAwait(false);

        if (keepTail && state.LiveMode == ViewerLiveMode.Tail)
        {
            ApplyViewportPosition(state, maximum);
            if (localReader is not null && localReader.TransientFailureVersion != failureVersion)
                ApplyViewportPosition(state, originalPosition);
            return;
        }

        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(
                0,
                state.TopByteOffset / BinaryBytesPerRow * BinaryBytesPerRow);
            state.TopWrappedSegmentIndex = 0;
        }
        else
        {
            state.TopByteOffset = await state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset, cancellationToken)
                .ConfigureAwait(false);

            if (state.WrapLines)
            {
                int segmentCount = await GetWrappedSegmentCountAsync(
                        sourcePath, state, state.TopByteOffset, width, cancellationToken)
                    .ConfigureAwait(false);
                state.TopWrappedSegmentIndex = Math.Clamp(
                    state.TopWrappedSegmentIndex,
                    0,
                    Math.Max(0, segmentCount - 1));
            }
            else
            {
                state.TopWrappedSegmentIndex = 0;
            }
        }

        var current = new ViewerViewportPosition(
            state.TopByteOffset,
            state.TopWrappedSegmentIndex);
        if (CompareViewportPositions(current, maximum) > 0)
            ApplyViewportPosition(state, maximum);

        if (localReader is not null && localReader.TransientFailureVersion != failureVersion)
            ApplyViewportPosition(state, originalPosition);
    }

    private async Task<bool> IsAtEndAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        ViewerViewportPosition maximum = await GetMaximumViewportPositionAsync(
                sourcePath, reader, state, contentHeight, width, cancellationToken)
            .ConfigureAwait(false);
        return state.TopByteOffset == maximum.ByteOffset &&
               state.TopWrappedSegmentIndex == maximum.WrappedSegmentIndex;
    }

    private async Task UpdateLiveModeAfterAwayNavigationAsync(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        if (state.LiveMode == ViewerLiveMode.Tail &&
            !await IsAtEndAsync(
                    sourcePath, reader, state, contentHeight, width, cancellationToken)
                .ConfigureAwait(false))
        {
            state.LiveMode = ViewerLiveMode.Watch;
        }
    }

    private async Task UpdateLiveModeAfterDownwardNavigationAsync(
        bool hasLocalSession,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        CancellationToken cancellationToken)
    {
        if (!hasLocalSession)
            return;

        if (await IsAtEndAsync(
                sourcePath, reader, state, contentHeight, width, cancellationToken)
            .ConfigureAwait(false))
        {
            state.LiveMode = ViewerLiveMode.Tail;
        }
        else if (state.LiveMode == ViewerLiveMode.Tail)
        {
            state.LiveMode = ViewerLiveMode.Watch;
        }
    }

    private readonly record struct ViewerNavigationRequest(
        ViewerNavigationKind Kind,
        long Value = 0);

    private enum ViewerNavigationKind
    {
        Up,
        Down,
        PageUp,
        PageDown,
        FastPageUp,
        FastPageDown,
        Home,
        End,
        ToggleWrap,
        ToggleWordWrap,
        ToggleViewMode,
        TogglePresentation,
        JumpPercent,
        JumpByte,
        JumpLine,
    }
}
