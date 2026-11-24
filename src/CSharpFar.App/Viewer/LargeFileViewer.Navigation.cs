using System.Globalization;
using System.Text;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private void MoveUp(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(0, state.TopByteOffset - BinaryBytesPerRow);
        }
        else if (state.WrapLines)
        {
            MoveWrappedUpOne(sourcePath, state, width);
        }
        else
        {
            state.TopByteOffset = state.LineScanner
                .FindPreviousLineStartAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MoveDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int contentHeight,
        int width)
    {
        if (state.IsHexMode)
        {
            state.TopByteOffset += BinaryBytesPerRow;
        }
        else if (state.WrapLines)
        {
            MoveWrappedDownOne(sourcePath, reader, state, width);
        }
        else
        {
            state.TopByteOffset = view.Lines.Count > 1
                ? view.Lines[1].StartOffset
                : view.NextOffset;
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageUp(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages)
    {
        int pageCount = Math.Max(1, pages);
        if (state.WrapLines && !state.IsHexMode)
        {
            int steps = Math.Max(1, contentHeight) * pageCount;
            for (int i = 0; i < steps; i++)
            {
                if (!MoveWrappedUpOne(sourcePath, state, width))
                    break;
            }
        }
        else
        {
            for (int page = 0; page < pageCount; page++)
            {
                if (state.IsHexMode)
                {
                    long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
                    state.TopByteOffset = Math.Max(0, state.TopByteOffset - delta);
                }
                else
                {
                    for (int i = 0; i < Math.Max(1, contentHeight); i++)
                    {
                        state.TopByteOffset = state.LineScanner
                            .FindPreviousLineStartAsync(state.TopByteOffset)
                            .GetAwaiter()
                            .GetResult();
                    }
                }
            }
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        LargeFileRenderView view,
        int contentHeight,
        int width)
    {
        if (state.WrapLines && !state.IsHexMode)
        {
            MovePageDown(sourcePath, reader, state, contentHeight, width, pages: 1);
            return;
        }

        if (state.IsHexMode)
        {
            long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
            state.TopByteOffset += delta;
        }
        else
        {
            state.TopByteOffset = view.NextOffset;
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MovePageDown(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        int pages)
    {
        int pageCount = Math.Max(1, pages);
        if (state.WrapLines && !state.IsHexMode)
        {
            int steps = Math.Max(1, contentHeight) * pageCount;
            for (int i = 0; i < steps; i++)
            {
                if (!MoveWrappedDownOne(sourcePath, reader, state, width))
                    break;
            }
        }
        else
        {
            for (int page = 0; page < pageCount; page++)
            {
                if (state.IsHexMode)
                {
                    long delta = (long)Math.Max(1, contentHeight) * BinaryBytesPerRow;
                    state.TopByteOffset += delta;
                }
                else
                {
                    var scanned = state.LineScanner
                        .ReadLinesAsync(
                            state.TopByteOffset,
                            Math.Max(1, contentHeight),
                            maxBytesPerLine: 256)
                        .GetAwaiter()
                        .GetResult();
                    state.TopByteOffset = scanned.NextOffset;
                }
            }
        }

        NormalizeViewport(sourcePath, reader, state, contentHeight, width, keepTail: false);
    }

    private void MoveToEnd(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width) =>
        ApplyViewportPosition(
            state,
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width));

    private bool MoveWrappedUpOne(
        string sourcePath,
        LargeFileViewerState state,
        int width)
    {
        if (state.TopWrappedSegmentIndex > 0)
        {
            state.TopWrappedSegmentIndex--;
            return true;
        }

        long previous = state.LineScanner
            .FindPreviousLineStartAsync(state.TopByteOffset)
            .GetAwaiter()
            .GetResult();
        if (previous == state.TopByteOffset)
            return false;

        state.TopByteOffset = previous;
        state.TopWrappedSegmentIndex = Math.Max(
            0,
            GetWrappedSegmentCount(sourcePath, state, previous, width) - 1);
        return true;
    }

    private bool MoveWrappedDownOne(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int width)
    {
        var scanned = state.LineScanner
            .ReadLinesAsync(state.TopByteOffset, 1, MaxWrappedLineCaptureBytes)
            .GetAwaiter()
            .GetResult();
        if (scanned.Lines.Count == 0)
            return false;

        ScannedLine line = scanned.Lines[0];
        int segmentCount = GetWrappedSegmentCount(sourcePath, state, line, width);
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

    private ViewerViewportPosition GetMaximumViewportPosition(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
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

        ViewerViewportPosition result = ComputeMaximumViewportPosition(
            sourcePath,
            state,
            length,
            contentHeight,
            width);

        if (localReader is null || localReader.TransientFailureVersion == failureVersion)
            _maximumViewportCache = new MaximumViewportCacheEntry(key, result);

        return result;
    }

    private ViewerViewportPosition ComputeMaximumViewportPosition(
        string sourcePath,
        LargeFileViewerState state,
        long length,
        int contentHeight,
        int width)
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
            long offset = state.LineScanner
                .FindTailTopOffsetAsync(visibleRows)
                .GetAwaiter()
                .GetResult();
            return new ViewerViewportPosition(offset, 0);
        }

        long lineStart = state.LineScanner
            .FindTailTopOffsetAsync(1)
            .GetAwaiter()
            .GetResult();
        int remainingRows = visibleRows;

        while (true)
        {
            int segmentCount = GetWrappedSegmentCount(sourcePath, state, lineStart, width);
            if (segmentCount >= remainingRows)
            {
                return new ViewerViewportPosition(
                    lineStart,
                    Math.Max(0, segmentCount - remainingRows));
            }

            remainingRows -= segmentCount;
            long previous = state.LineScanner
                .FindPreviousLineStartAsync(lineStart)
                .GetAwaiter()
                .GetResult();
            if (previous == lineStart)
                return new ViewerViewportPosition(lineStart, 0);

            lineStart = previous;
        }
    }

    private int GetWrappedSegmentCount(
        string sourcePath,
        LargeFileViewerState state,
        long lineStart,
        int width)
    {
        var scanned = state.LineScanner
            .ReadLinesAsync(lineStart, 1, MaxWrappedLineCaptureBytes)
            .GetAwaiter()
            .GetResult();
        return scanned.Lines.Count == 0
            ? 1
            : GetWrappedSegmentCount(sourcePath, state, scanned.Lines[0], width);
    }

    private int GetWrappedSegmentCount(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width)
    {
        if (RequiresStreamingWrappedLayout(line))
        {
            return GetStreamingWrappedLineLayout(
                    state,
                    line,
                    width,
                    captureStartSegment: 0,
                    captureSegmentCount: 0)
                .SegmentCount;
        }

        return GetOrCreateWrappedLineLayout(sourcePath, state, line, width).Segments.Count;
    }

    private WrappedLineLayout GetOrCreateWrappedLineLayout(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width)
    {
        if (TryGetWrappedLineLayout(sourcePath, state, line, width, out var cached))
            return cached;

        PresentedLine presented = state.Presentation.Present(
            state.PresentationMode,
            sourcePath,
            state.LineScanner,
            [line],
            Math.Max(1, width))[0];
        presented = ResolvePresentationForSearch(presented, state.SearchMatch);

        if (presented.PreserveLineLayout)
            ClearWrappedLayoutCache();

        WrappedLineLayout layout = CreateWrappedLineLayout(presented, state.WordWrap, width);
        StoreWrappedLineLayout(
            CreateWrappedLayoutCacheKey(sourcePath, state, line, width),
            layout);
        return layout;
    }

    private static WrappedLineLayout CreateWrappedLineLayout(
        PresentedLine presented,
        bool wordWrap,
        int width)
    {
        WrappedTextSegment[] segments = presented.PreserveLineLayout
            ? [new WrappedTextSegment(0, presented.Text)]
            : SplitWrappedLine(presented.Text, Math.Max(1, width), wordWrap).ToArray();
        return new WrappedLineLayout(presented, segments);
    }

    private bool TryGetWrappedLineLayout(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width,
        out WrappedLineLayout layout)
    {
        EnsureLayoutCacheContext(state);
        return _wrappedLayoutCache.TryGet(
            CreateWrappedLayoutCacheKey(sourcePath, state, line, width),
            out layout);
    }

    private void StoreWrappedLineLayout(
        WrappedLayoutCacheKey key,
        WrappedLineLayout layout) =>
        _wrappedLayoutCache.Set(key, layout);

    private WrappedLayoutCacheKey CreateWrappedLayoutCacheKey(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width)
    {
        ViewerSearchMatch? match = state.SearchMatch is { IsHex: false } candidate &&
                                   candidate.LineStartOffset == line.StartOffset
            ? candidate
            : null;
        return new WrappedLayoutCacheKey(
            state.LineScanner,
            sourcePath,
            line.StartOffset,
            line.NextOffset,
            Math.Max(1, width),
            state.WordWrap,
            state.PresentationMode,
            match?.CharacterIndex ?? -1,
            match?.CharacterLength ?? 0);
    }

    private MaximumViewportCacheKey CreateMaximumViewportCacheKey(
        string sourcePath,
        LargeFileViewerState state,
        long sourceLength,
        int contentHeight,
        int width)
    {
        ViewerSearchMatch? match = state.SearchMatch is { IsHex: false } candidate
            ? candidate
            : null;
        return new MaximumViewportCacheKey(
            state.LineScanner,
            sourcePath,
            sourceLength,
            Math.Max(1, contentHeight),
            Math.Max(1, width),
            state.ViewMode,
            state.WrapLines,
            state.WordWrap,
            state.PresentationMode,
            match?.LineStartOffset ?? -1,
            match?.CharacterIndex ?? -1,
            match?.CharacterLength ?? 0);
    }

    private void EnsureLayoutCacheContext(LargeFileViewerState state)
    {
        if (ReferenceEquals(_layoutCacheState, state) &&
            ReferenceEquals(_layoutCacheScanner, state.LineScanner))
        {
            return;
        }

        _layoutCacheState = state;
        _layoutCacheScanner = state.LineScanner;
        ClearWrappedLayoutCache();
        _maximumViewportCache = null;
    }

    private void ClearWrappedLayoutCache() => _wrappedLayoutCache.Clear();

    private static bool RequiresStreamingWrappedLayout(ScannedLine line) =>
        line.NextOffset - line.StartOffset > MaxWrappedLineCaptureBytes;

    private LongWrappedLineLayout GetStreamingWrappedLineLayout(
        LargeFileViewerState state,
        ScannedLine line,
        int width,
        int captureStartSegment,
        int captureSegmentCount)
    {
        var builder = new StreamingWrappedLineBuilder(
            Math.Max(1, width),
            state.WordWrap,
            captureStartSegment,
            captureSegmentCount);
        state.LineScanner
            .VisitLineTextChunksAsync(line, builder.Append)
            .GetAwaiter()
            .GetResult();
        return builder.Complete();
    }

    private void WriteRawWrappedTextLine(
        IUiCanvas canvas,
        string text,
        int y,
        int width)
    {
        var layout = new ViewerTextLayout(text);
        canvas.WriteForced(
            0,
            y,
            layout.Slice(0, Math.Max(1, width)),
            CSharpFarPaletteStyles.CommandLine(_palette));
    }

    private void NormalizeViewport(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        bool keepTail = true)
    {
        var localReader = reader as ILocalFileByteReader;
        int failureVersion = localReader?.TransientFailureVersion ?? 0;
        var originalPosition = new ViewerViewportPosition(
            state.TopByteOffset,
            state.TopWrappedSegmentIndex);

        ViewerViewportPosition maximum =
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width);

        if (keepTail && state.LiveMode == ViewerLiveMode.Tail)
        {
            ApplyViewportPosition(state, maximum);
            if (localReader is not null && localReader.TransientFailureVersion != failureVersion)
                ApplyViewportPosition(state, originalPosition);
            return;
        }

        if (state.IsHexMode)
        {
            state.TopByteOffset = Math.Max(0, state.TopByteOffset / BinaryBytesPerRow * BinaryBytesPerRow);
            state.TopWrappedSegmentIndex = 0;
        }
        else
        {
            state.TopByteOffset = state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();

            if (state.WrapLines)
            {
                int segmentCount =
                    GetWrappedSegmentCount(sourcePath, state, state.TopByteOffset, width);
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

    private bool IsAtEnd(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        ViewerViewportPosition maximum =
            GetMaximumViewportPosition(sourcePath, reader, state, contentHeight, width);
        return state.TopByteOffset == maximum.ByteOffset &&
               state.TopWrappedSegmentIndex == maximum.WrappedSegmentIndex;
    }

    private static void SetLiveMode(
        LocalViewerSession? session,
        LargeFileViewerState state,
        ViewerLiveMode newMode)
    {
        ViewerLiveMode oldMode = state.LiveMode;
        if (oldMode == newMode)
            return;

        if (newMode != ViewerLiveMode.Off && session is null)
            return;

        if (oldMode == ViewerLiveMode.Off && newMode != ViewerLiveMode.Off)
            session!.StartMonitoring();
        else if (oldMode != ViewerLiveMode.Off && newMode == ViewerLiveMode.Off)
            session?.StopMonitoring();

        state.LiveMode = newMode;
    }

    private void UpdateLiveModeAfterAwayNavigation(
        LocalViewerSession? localSession,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (state.LiveMode == ViewerLiveMode.Tail &&
            !IsAtEnd(sourcePath, reader, state, contentHeight, width))
        {
            SetLiveMode(localSession, state, ViewerLiveMode.Watch);
        }
    }

    private void UpdateLiveModeAfterDownwardNavigation(
        LocalViewerSession? localSession,
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        if (localSession is null)
            return;

        if (IsAtEnd(sourcePath, reader, state, contentHeight, width))
        {
            SetLiveMode(localSession, state, ViewerLiveMode.Tail);
        }
        else if (state.LiveMode == ViewerLiveMode.Tail)
        {
            SetLiveMode(localSession, state, ViewerLiveMode.Watch);
        }
    }

    private static int CompareViewportPositions(
        ViewerViewportPosition left,
        ViewerViewportPosition right)
    {
        int byteComparison = left.ByteOffset.CompareTo(right.ByteOffset);
        return byteComparison != 0
            ? byteComparison
            : left.WrappedSegmentIndex.CompareTo(right.WrappedSegmentIndex);
    }

    private static void ApplyViewportPosition(
        LargeFileViewerState state,
        ViewerViewportPosition position)
    {
        state.TopByteOffset = position.ByteOffset;
        state.TopWrappedSegmentIndex = position.WrappedSegmentIndex;
    }

    private void MoveHorizontal(LargeFileViewerState state, int delta)
    {
        if (state.WrapLines)
            return;

        state.HorizontalOffset = Math.Max(0, state.HorizontalOffset + delta);
    }

    private static void MoveHorizontalToCurrentLineEnd(
        LargeFileViewerState state,
        LargeFileRenderView view,
        int width)
    {
        if (state.WrapLines || view.Lines.Count == 0)
            return;

        int lineLength = new ViewerTextLayout(view.Lines[0].Text).CellWidth;
        state.HorizontalOffset = Math.Max(0, lineLength - Math.Max(1, width));
    }

    private void JumpToPosition(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        LocalViewerSession? localSession)
    {
        string? input = _dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = "Viewer",
            Prompt = state.IsHexMode ? "Percent or byte offset:" : "Line number or percent:",
            Validate = text => ValidateJump(text, state.IsHexMode),
        });
        if (input is null)
            return;

        ViewerNavigationRequest request;
        if (input.EndsWith('%'))
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpPercent,
                int.Parse(input[..^1], CultureInfo.InvariantCulture));
        }
        else if (state.IsHexMode)
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpByte,
                long.Parse(input, CultureInfo.InvariantCulture));
        }
        else
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpLine,
                long.Parse(input, CultureInfo.InvariantCulture));
        }

        QueueNavigation(
            sourcePath,
            reader,
            state,
            contentHeight,
            width,
            localSession,
            request);
    }

    private static void ToggleViewMode(LargeFileViewerState state)
    {
        if (state.IsHexMode)
        {
            state.ViewMode = LargeFileViewMode.Text;
            state.TopByteOffset = state.LineScanner
                .FindLineStartAtOrBeforeAsync(state.TopByteOffset)
                .GetAwaiter()
                .GetResult();
        }
        else
        {
            state.ViewMode = LargeFileViewMode.Hex;
            state.TopByteOffset = 0;
        }

        state.TopWrappedSegmentIndex = 0;
        state.HorizontalOffset = 0;
        state.SearchMatch = null;
    }
}
